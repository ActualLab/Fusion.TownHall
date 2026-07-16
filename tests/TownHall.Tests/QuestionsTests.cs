namespace TownHall.Tests;

public abstract class QuestionsTests(TestAppHost host) : TestBase(host)
{
    [Fact]
    public async Task OnPostRequiresLiveRoom()
    {
        var owner = await NewUser();
        var room = await CreateRoom(owner, live: false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Call(new Questions_Post(owner, room.Id, "Anyone there?")));
    }

    [Fact]
    public async Task OnPostAssignsIndexesAndReferencesAuthorById()
    {
        var owner = await NewUser();
        var other = await NewUser();
        var room = await CreateRoom(owner);
        await Call(new Users_SetName(owner, "Poster"));
        var q1 = await Call(new Questions_Post(owner, room.Id, "First?"));
        var q2 = await Call(new Questions_Post(owner, room.Id, "  Second?  "));
        Assert.Equal(1, q1.Index);
        Assert.Equal(2, q2.Index);
        Assert.Equal("Second?", q2.Text);
        // Questions reference the author by a stable id (not a name snapshot); same author -> same id
        Assert.Equal(q1.AuthorId, q2.AuthorId);
        Assert.Equal("Poster", (await Users.Get(owner, q1.AuthorId))!.Name);
        var q3 = await Call(new Questions_Post(other, room.Id, "Third?"));
        Assert.NotEqual(q1.AuthorId, q3.AuthorId);
    }

    [Fact]
    public async Task RenamePropagatesToAlreadyPostedQuestions()
    {
        var owner = await NewUser();
        var room = await CreateRoom(owner);
        await Call(new Users_SetName(owner, "Poster"));
        var q = await Call(new Questions_Post(owner, room.Id, "Mine?"));
        Assert.Equal("Poster", (await Users.Get(owner, q.AuthorId))!.Name);
        await Call(new Users_SetName(owner, "Renamed"));
        Assert.Equal("Renamed", (await ReadWhen(() => Users.Get(owner, q.AuthorId), u => u!.Name == "Renamed"))!.Name);
    }

    [Fact]
    public async Task OnPostCollapsesWhitespaceToSingleParagraph()
    {
        var owner = await NewUser();
        var room = await CreateRoom(owner);
        var question = await Call(new Questions_Post(owner, room.Id, "  Line one\n\r\n  line\ttwo   ok? "));
        Assert.Equal("Line one line two ok?", question.Text);
    }

    [Fact]
    public async Task ListOpenAndListTopOpenOrdering()
    {
        var owner = await NewUser();
        var voter = await NewUser();
        var room = await CreateRoom(owner);
        var q1 = await Call(new Questions_Post(owner, room.Id, "Older?"));
        var q2 = await Call(new Questions_Post(owner, room.Id, "Newer?"));
        Assert.Equal(q1, await Questions.Get(owner, room.Id, q1.Index));
        Assert.Equal(new[] { q2.Index, q1.Index }, await Questions.ListOpen(owner, room.Id));
        await Call(new Questions_Vote(voter, room.Id, q1.Index, true));
        Assert.Equal(new[] { q1.Index, q2.Index }, await Questions.ListTopOpen(owner, room.Id, 10));
        Assert.Equal(new[] { q1.Index }, await Questions.ListTopOpen(owner, room.Id, 1));
    }

    [Fact]
    public async Task OnVoteSetsAndClearsVote()
    {
        var owner = await NewUser();
        var voter = await NewUser();
        var room = await CreateRoom(owner);
        var q = await Call(new Questions_Post(owner, room.Id, "Votes?"));
        await Call(new Questions_Vote(voter, room.Id, q.Index, true));
        Assert.Equal(1, await Questions.GetVoteCount(voter, room.Id, q.Index));
        Assert.True(await Questions.HasOwnVote(voter, room.Id, q.Index));
        Assert.False(await Questions.HasOwnVote(owner, room.Id, q.Index));
        await Call(new Questions_Vote(voter, room.Id, q.Index, false));
        Assert.Equal(0, await ReadWhen(() => Questions.GetVoteCount(voter, room.Id, q.Index), c => c == 0));
        Assert.False(await ReadWhen(() => Questions.HasOwnVote(voter, room.Id, q.Index), v => !v));
    }

    [Fact]
    public async Task OnVoteRequiresLiveRoomAndOpenQuestion()
    {
        var owner = await NewUser();
        var room = await CreateRoom(owner);
        var q1 = await Call(new Questions_Post(owner, room.Id, "Resolved?"));
        var q2 = await Call(new Questions_Post(owner, room.Id, "Stopped?"));
        await Call(new Questions_Resolve(owner, room.Id, q1.Index, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Call(new Questions_Vote(owner, room.Id, q1.Index, true)));
        await Call(new Rooms_SetLive(owner, room.Id, false));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Call(new Questions_Vote(owner, room.Id, q2.Index, true)));
    }

    [Fact]
    public async Task OnResolveMovesQuestionToResolved()
    {
        var owner = await NewUser();
        var other = await NewUser();
        var room = await CreateRoom(owner);
        var q = await Call(new Questions_Post(owner, room.Id, "Resolve me?"));
        await Call(new Questions_Vote(other, room.Id, q.Index, true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Call(new Questions_Resolve(other, room.Id, q.Index, "nope")));
        await Call(new Questions_Resolve(owner, room.Id, q.Index, "  Done  "));
        Assert.Equal(new[] { q.Index }, await Questions.ListResolved(owner, room.Id));
        Assert.Empty(await Questions.ListOpen(owner, room.Id));
        Assert.Equal("Done", (await Questions.GetResolution(owner, room.Id, q.Index))!.Note);
        Assert.Equal(1, await Questions.GetVoteCount(owner, room.Id, q.Index)); // Votes are frozen
    }

    [Fact]
    public async Task ResolutionNoteEditableAfterEndedAndPreservesTime()
    {
        var owner = await NewUser();
        var room = await CreateRoom(owner);
        var q = await Call(new Questions_Post(owner, room.Id, "Resolve later?"));
        // Mark resolved with no note
        await Call(new Questions_Resolve(owner, room.Id, q.Index, ""));
        var resolvedAt = (await Questions.GetResolution(owner, room.Id, q.Index))!.ResolvedAt;

        // End the room, then add the note after the fact
        await Call(new Rooms_AdjustDuration(owner, room.Id, TimeSpan.FromHours(-2)));
        await ReadWhen(() => Rooms.Get(owner, room.Id), r => r!.Status == RoomStatus.Ended);
        await Call(new Questions_Resolve(owner, room.Id, q.Index, "  Answered\nlater  "));
        var res = await ReadWhen(() => Questions.GetResolution(owner, room.Id, q.Index), r => r!.Note.Length > 0);
        Assert.Equal("Answered later", res!.Note); // Single paragraph
        Assert.Equal(resolvedAt, res.ResolvedAt);  // Original resolution time preserved
    }

    [Fact]
    public async Task OnDeleteRemovesEverything()
    {
        var owner = await NewUser();
        var voter = await NewUser();
        var room = await CreateRoom(owner);
        var q = await Call(new Questions_Post(owner, room.Id, "Delete me?"));
        await Call(new Questions_Vote(voter, room.Id, q.Index, true));
        await Call(new Questions_Delete(owner, room.Id, q.Index));
        Assert.Null(await Questions.Get(owner, room.Id, q.Index));
        Assert.Empty(await Questions.ListOpen(owner, room.Id));
        Assert.Equal(0, await Questions.GetVoteCount(owner, room.Id, q.Index));
        Assert.False(await Questions.HasOwnVote(voter, room.Id, q.Index));
        await Call(new Questions_Delete(owner, room.Id, q.Index)); // Idempotent
    }

    [Fact]
    public async Task AnonymousPostUsesPerRoomPseudonym()
    {
        var owner = await NewUser();
        var poster = await NewUser("Real Name");
        var room = await CreateRoom(owner);
        var posterId = (await Users.GetOwnUserId(poster))!;

        var pub = await Call(new Questions_Post(poster, room.Id, "Public?"));
        var anon1 = await Call(new Questions_Post(poster, room.Id, "Secret 1?", Anonymous: true));
        var anon2 = await Call(new Questions_Post(poster, room.Id, "Secret 2?", Anonymous: true));

        // A public post is attributed to the real user; anonymous posts are not
        Assert.Equal(posterId, pub.AuthorId);
        Assert.NotEqual(posterId, anon1.AuthorId);
        Assert.StartsWith("anon-", anon1.AuthorId);
        // Same (user, room) -> one stable pseudonym across posts
        Assert.Equal(anon1.AuthorId, anon2.AuthorId);
        // The pseudonym resolves to a generated name that isn't the real one
        var anonName = (await Users.Get(poster, anon1.AuthorId))!.Name;
        Assert.Matches(@"^\w+ \w+$", anonName);
        Assert.NotEqual("Real Name", anonName);
        // The real account name is unaffected
        Assert.Equal("Real Name", (await Users.Get(poster, posterId))!.Name);
    }
}

public sealed class QuestionsServerTests(TestAppHost host) : QuestionsTests(host)
{
    protected override IServiceProvider TestServices => Host.Services;
}

public sealed class QuestionsClientTests(TestAppHost host) : QuestionsTests(host)
{
    protected override IServiceProvider TestServices => Host.ClientServices;
}

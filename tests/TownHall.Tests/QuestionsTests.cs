namespace TownHall.Tests;

public sealed class QuestionsTests(TestAppHost host) : TestBase(host)
{
    [Fact]
    public async Task PostRequiresLiveRoom()
    {
        var owner = await NewUser();
        var room = await CreateRoom(owner, live: false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => For(owner).Questions.Post(new Questions_Post(room.Id, "Anyone there?")));
    }

    [Fact]
    public async Task PostAssignsIndexesAndReferencesAuthorById()
    {
        var owner = await NewUser();
        var other = await NewUser();
        var room = await CreateRoom(owner);
        await For(owner).Users.SetName(new Users_SetName("Poster"));
        var q1 = await For(owner).Questions.Post(new Questions_Post(room.Id, "First?"));
        var q2 = await For(owner).Questions.Post(new Questions_Post(room.Id, "  Second?  "));
        Assert.Equal(1, q1.Index);
        Assert.Equal(2, q2.Index);
        Assert.Equal("Second?", q2.Text);
        // Questions reference the author by a stable id (not a name snapshot); same author -> same id
        Assert.Equal(q1.AuthorId, q2.AuthorId);
        Assert.Equal("Poster", (await GetQuestion(owner, room.Id, q1.Index))!.AuthorName);
        var q3 = await For(other).Questions.Post(new Questions_Post(room.Id, "Third?"));
        Assert.NotEqual(q1.AuthorId, q3.AuthorId);
    }

    [Fact]
    public async Task RenamePropagatesToAlreadyPostedQuestions()
    {
        var owner = await NewUser();
        var room = await CreateRoom(owner);
        await For(owner).Users.SetName(new Users_SetName("Poster"));
        var q = await For(owner).Questions.Post(new Questions_Post(room.Id, "Mine?"));
        Assert.Equal("Poster", (await GetQuestion(owner, room.Id, q.Index))!.AuthorName);
        await For(owner).Users.SetName(new Users_SetName("Renamed"));
        Assert.Equal("Renamed", (await GetQuestion(owner, room.Id, q.Index))!.AuthorName);
    }

    [Fact]
    public async Task PostCollapsesWhitespaceToSingleParagraph()
    {
        var owner = await NewUser();
        var room = await CreateRoom(owner);
        var question = await For(owner).Questions.Post(new Questions_Post(room.Id, "  Line one\n\r\n  line\ttwo   ok? "));
        Assert.Equal("Line one line two ok?", question.Text);
    }

    [Fact]
    public async Task ListOpenAndTopOrdering()
    {
        var owner = await NewUser();
        var voter = await NewUser();
        var room = await CreateRoom(owner);
        var q1 = await For(owner).Questions.Post(new Questions_Post(room.Id, "Older?"));
        var q2 = await For(owner).Questions.Post(new Questions_Post(room.Id, "Newer?"));
        Assert.Equal(q1, (await GetQuestion(owner, room.Id, q1.Index))!.Question);
        // Open = newest first
        Assert.Equal(new[] { q2.Index, q1.Index }, (await GetOpen(owner, room.Id)).Select(v => v.Question.Index));
        await For(voter).Questions.Vote(new Questions_Vote(room.Id, q1.Index, true));
        // "Top" = most votes first, ties older-first
        var top = (await GetOpen(owner, room.Id))
            .OrderByDescending(v => v.VoteCount).ThenBy(v => v.Question.Index)
            .Select(v => v.Question.Index).ToArray();
        Assert.Equal(new[] { q1.Index, q2.Index }, top);
    }

    [Fact]
    public async Task VoteSetsAndClearsVote()
    {
        var owner = await NewUser();
        var voter = await NewUser();
        var room = await CreateRoom(owner);
        var q = await For(owner).Questions.Post(new Questions_Post(room.Id, "Votes?"));
        await For(voter).Questions.Vote(new Questions_Vote(room.Id, q.Index, true));
        var voted = await GetQuestion(voter, room.Id, q.Index);
        Assert.Equal(1, voted!.VoteCount);
        Assert.True(voted.HasOwnVote);
        Assert.False((await GetQuestion(owner, room.Id, q.Index))!.HasOwnVote);
        await For(voter).Questions.Vote(new Questions_Vote(room.Id, q.Index, false));
        var cleared = await GetQuestion(voter, room.Id, q.Index);
        Assert.Equal(0, cleared!.VoteCount);
        Assert.False(cleared.HasOwnVote);
    }

    [Fact]
    public async Task VoteRequiresLiveRoomAndOpenQuestion()
    {
        var owner = await NewUser();
        var room = await CreateRoom(owner);
        var q1 = await For(owner).Questions.Post(new Questions_Post(room.Id, "Resolved?"));
        var q2 = await For(owner).Questions.Post(new Questions_Post(room.Id, "Stopped?"));
        await For(owner).Questions.Resolve(new Questions_Resolve(room.Id, q1.Index, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => For(owner).Questions.Vote(new Questions_Vote(room.Id, q1.Index, true)));
        await For(owner).Rooms.SetLive(new Rooms_SetLive(room.Id, false));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => For(owner).Questions.Vote(new Questions_Vote(room.Id, q2.Index, true)));
    }

    [Fact]
    public async Task ResolveMovesQuestionToResolved()
    {
        var owner = await NewUser();
        var other = await NewUser();
        var room = await CreateRoom(owner);
        var q = await For(owner).Questions.Post(new Questions_Post(room.Id, "Resolve me?"));
        await For(other).Questions.Vote(new Questions_Vote(room.Id, q.Index, true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => For(other).Questions.Resolve(new Questions_Resolve(room.Id, q.Index, "nope")));
        await For(owner).Questions.Resolve(new Questions_Resolve(room.Id, q.Index, "  Done  "));
        Assert.Equal(new[] { q.Index }, (await GetResolved(owner, room.Id)).Select(v => v.Question.Index));
        Assert.Empty(await GetOpen(owner, room.Id));
        var resolved = await GetQuestion(owner, room.Id, q.Index);
        Assert.Equal("Done", resolved!.Resolution!.Note);
        Assert.Equal(1, resolved.VoteCount); // Votes are frozen
    }

    [Fact]
    public async Task ResolutionNoteEditableAfterEndedAndPreservesTime()
    {
        var owner = await NewUser();
        var room = await CreateRoom(owner);
        var q = await For(owner).Questions.Post(new Questions_Post(room.Id, "Resolve later?"));
        // Mark resolved with no note
        await For(owner).Questions.Resolve(new Questions_Resolve(room.Id, q.Index, ""));
        var resolvedAt = (await GetQuestion(owner, room.Id, q.Index))!.Resolution!.ResolvedAt;

        // End the room, then add the note after the fact
        await For(owner).Rooms.AdjustDuration(new Rooms_AdjustDuration(room.Id, TimeSpan.FromHours(-2)));
        Assert.Equal(RoomStatus.Ended, (await GetRoom(owner, room.Id))!.Status);
        await For(owner).Questions.Resolve(new Questions_Resolve(room.Id, q.Index, "  Answered\nlater  "));
        var res = (await GetQuestion(owner, room.Id, q.Index))!.Resolution;
        Assert.Equal("Answered later", res!.Note); // Single paragraph
        Assert.Equal(resolvedAt, res.ResolvedAt);  // Original resolution time preserved
    }

    [Fact]
    public async Task DeleteRemovesEverything()
    {
        var owner = await NewUser();
        var voter = await NewUser();
        var room = await CreateRoom(owner);
        var q = await For(owner).Questions.Post(new Questions_Post(room.Id, "Delete me?"));
        await For(voter).Questions.Vote(new Questions_Vote(room.Id, q.Index, true));
        await For(owner).Questions.Delete(new Questions_Delete(room.Id, q.Index));
        Assert.Null(await GetQuestion(owner, room.Id, q.Index));
        Assert.Empty(await GetOpen(owner, room.Id));
        await For(owner).Questions.Delete(new Questions_Delete(room.Id, q.Index)); // Idempotent
    }

    [Fact]
    public async Task AnonymousPostUsesPerRoomPseudonym()
    {
        var owner = await NewUser();
        var poster = await NewUser("Real Name");
        var room = await CreateRoom(owner);
        var posterId = (await GetOwn(poster))!.Id;

        var pub = await For(poster).Questions.Post(new Questions_Post(room.Id, "Public?"));
        var anon1 = await For(poster).Questions.Post(new Questions_Post(room.Id, "Secret 1?", Anonymous: true));
        var anon2 = await For(poster).Questions.Post(new Questions_Post(room.Id, "Secret 2?", Anonymous: true));

        // A public post is attributed to the real user; anonymous posts are not
        Assert.Equal(posterId, pub.AuthorId);
        Assert.NotEqual(posterId, anon1.AuthorId);
        Assert.StartsWith("anon-", anon1.AuthorId);
        // Same (user, room) -> one stable pseudonym across posts
        Assert.Equal(anon1.AuthorId, anon2.AuthorId);
        // The pseudonym resolves to a generated name that isn't the real one
        var anonName = (await GetQuestion(poster, room.Id, anon1.Index))!.AuthorName;
        Assert.Matches(@"^\w+ \w+$", anonName);
        Assert.NotEqual("Real Name", anonName);
        // The real account name is unaffected
        Assert.Equal("Real Name", (await GetQuestion(poster, room.Id, pub.Index))!.AuthorName);
    }
}

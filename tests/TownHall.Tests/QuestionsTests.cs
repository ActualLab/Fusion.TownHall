namespace TownHall.Tests;

public abstract class QuestionsTests(TestAppHost host) : TestBase(host)
{
    [Fact]
    public async Task OnPostRequiresLiveRoom()
    {
        var owner = Session.New();
        var room = await CreateRoom(owner, live: false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Call(new Questions_Post(owner, room.Id, "Anyone there?")));
    }

    [Fact]
    public async Task OnPostAssignsIndexesAndAuthorNameSnapshot()
    {
        var owner = Session.New();
        var room = await CreateRoom(owner);
        await Call(new Participants_SetName(owner, "Poster"));
        var q1 = await Call(new Questions_Post(owner, room.Id, "First?"));
        var q2 = await Call(new Questions_Post(owner, room.Id, "  Second?  "));
        Assert.Equal(1, q1.Index);
        Assert.Equal(2, q2.Index);
        Assert.Equal("Poster", q1.AuthorName);
        Assert.Equal("Second?", q2.Text);
        await Call(new Participants_SetName(owner, "Renamed"));
        Assert.Equal("Poster", (await Questions.Get(owner, room.Id, q1.Index))!.AuthorName);
    }

    [Fact]
    public async Task OnPostCollapsesWhitespaceToSingleParagraph()
    {
        var owner = Session.New();
        var room = await CreateRoom(owner);
        var question = await Call(new Questions_Post(owner, room.Id, "  Line one\n\r\n  line\ttwo   ok? "));
        Assert.Equal("Line one line two ok?", question.Text);
    }

    [Fact]
    public async Task ListOpenIdsAndGetTopOpenIdsOrdering()
    {
        var owner = Session.New();
        var voter = Session.New();
        var room = await CreateRoom(owner);
        var q1 = await Call(new Questions_Post(owner, room.Id, "Older?"));
        var q2 = await Call(new Questions_Post(owner, room.Id, "Newer?"));
        Assert.Equal(q1, await Questions.Get(owner, room.Id, q1.Index));
        Assert.Equal(new[] { q2.Index, q1.Index }, await Questions.ListOpenIds(owner, room.Id));
        await Call(new Questions_Vote(voter, room.Id, q1.Index, true));
        Assert.Equal(new[] { q1.Index, q2.Index }, await Questions.GetTopOpenIds(owner, room.Id, 10));
        Assert.Equal(new[] { q1.Index }, await Questions.GetTopOpenIds(owner, room.Id, 1));
    }

    [Fact]
    public async Task OnVoteSetsAndClearsVote()
    {
        var owner = Session.New();
        var voter = Session.New();
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
        var owner = Session.New();
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
        var owner = Session.New();
        var other = Session.New();
        var room = await CreateRoom(owner);
        var q = await Call(new Questions_Post(owner, room.Id, "Resolve me?"));
        await Call(new Questions_Vote(other, room.Id, q.Index, true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Call(new Questions_Resolve(other, room.Id, q.Index, "nope")));
        await Call(new Questions_Resolve(owner, room.Id, q.Index, "  Done  "));
        Assert.Equal(new[] { q.Index }, await Questions.ListResolvedIds(owner, room.Id));
        Assert.Empty(await Questions.ListOpenIds(owner, room.Id));
        Assert.Equal("Done", (await Questions.GetResolution(owner, room.Id, q.Index))!.Note);
        Assert.Equal(1, await Questions.GetVoteCount(owner, room.Id, q.Index)); // Votes are frozen
    }

    [Fact]
    public async Task OnDeleteRemovesEverything()
    {
        var owner = Session.New();
        var voter = Session.New();
        var room = await CreateRoom(owner);
        var q = await Call(new Questions_Post(owner, room.Id, "Delete me?"));
        await Call(new Questions_Vote(voter, room.Id, q.Index, true));
        await Call(new Questions_Delete(owner, room.Id, q.Index));
        Assert.Null(await Questions.Get(owner, room.Id, q.Index));
        Assert.Empty(await Questions.ListOpenIds(owner, room.Id));
        Assert.Equal(0, await Questions.GetVoteCount(owner, room.Id, q.Index));
        Assert.False(await Questions.HasOwnVote(voter, room.Id, q.Index));
        await Call(new Questions_Delete(owner, room.Id, q.Index)); // Idempotent
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

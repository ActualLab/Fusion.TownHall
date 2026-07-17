namespace TownHall.Tests;

// Proves the ChangeTracker -> stream re-yield path: an open stream re-emits an updated value after an
// external change made by another session, not just the current value on subscribe.
public sealed class PropagationTests(TestAppHost host) : TestBase(host)
{
    [Fact]
    public async Task ListOpenReYieldsNewQuestion()
    {
        var owner = await NewUser();
        var viewer = await NewUser();
        var poster = await NewUser();
        var room = await CreateRoom(owner);
        var open = await NextAfter(
            For(viewer).Questions.ListOpen(room.Id),
            () => For(poster).Questions.Post(new Questions_Post(room.Id, "Anyone there?")));
        Assert.Equal("Anyone there?", Assert.Single(open).Question.Text);
    }

    [Fact]
    public async Task ListOpenReYieldsIncrementedVoteCount()
    {
        var owner = await NewUser();
        var viewer = await NewUser();
        var voter = await NewUser();
        var room = await CreateRoom(owner);
        var q = await For(owner).Questions.Post(new Questions_Post(room.Id, "Votes?"));
        var open = await NextAfter(
            For(viewer).Questions.ListOpen(room.Id),
            () => For(voter).Questions.Vote(new Questions_Vote(room.Id, q.Index, true)));
        Assert.Equal(1, open.Single(v => v.Question.Index == q.Index).VoteCount);
    }

    [Fact]
    public async Task RoomViewReYieldsAudienceCount()
    {
        var owner = await NewUser();
        var viewer = await NewUser();
        var watcher = await NewUser();
        var room = await CreateRoom(owner);
        var view = await NextAfter(
            For(viewer).Rooms.Get(room.Id),
            () => For(watcher).Presence.Watch(new Presence_Watch(room.Id)));
        Assert.Equal(1, view!.Stats.AudienceCount);
    }

    [Fact]
    public async Task ListRoomsReYieldsNewPublicRoom()
    {
        var owner = await NewUser();
        var viewer = await NewUser();
        Room room = null!;
        var ids = await NextAfter(
            For(viewer).Rooms.ListRooms(100),
            async () => room = await For(owner).Rooms.Create(new Rooms_Create("Fresh Room", TimeSpan.FromHours(1))));
        Assert.Contains(room.Id, ids);
    }

    [Fact]
    public async Task RoomCardReYieldsQuestionCountAndAudience()
    {
        var owner = await NewUser();
        var viewer = await NewUser();
        var room = await CreateRoom(owner);
        var card = await NextAfter(
            For(viewer).Rooms.GetCard(room.Id),
            async () => {
                await For(owner).Questions.Post(new Questions_Post(room.Id, "Counted?"));
                await For(owner).Presence.Watch(new Presence_Watch(room.Id));
            });
        Assert.True(card!.QuestionCount >= 1);
    }

    // Private methods

    private static async Task<T> NextAfter<T>(IAsyncEnumerable<T> stream, Func<Task> change)
    {
        using var cts = new CancellationTokenSource(WaitTimeout);
        await using var e = stream.GetAsyncEnumerator(cts.Token);
        await e.MoveNextAsync(); // Current value (subscribe)
        await change();          // External mutation by another session
        await e.MoveNextAsync(); // Must re-yield after the change
        return e.Current;
    }
}

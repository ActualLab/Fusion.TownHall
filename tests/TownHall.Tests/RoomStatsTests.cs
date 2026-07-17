namespace TownHall.Tests;

public sealed class RoomStatsTests(TestAppHost host) : TestBase(host)
{
    [Fact]
    public async Task StatsCounts()
    {
        var owner = await NewUser();
        var voter = await NewUser();
        var room = await CreateRoom(owner);
        var q1 = await For(owner).Questions.Post(new Questions_Post(room.Id, "Open?"));
        var q2 = await For(owner).Questions.Post(new Questions_Post(room.Id, "Resolved?"));
        await For(voter).Questions.Vote(new Questions_Vote(room.Id, q1.Index, true));
        await For(owner).Questions.Vote(new Questions_Vote(room.Id, q2.Index, true));
        await For(owner).Questions.Resolve(new Questions_Resolve(room.Id, q2.Index, ""));
        await For(voter).Presence.Watch(new Presence_Watch(room.Id));
        Assert.Equal(new RoomStats(1, 1, 2, 1), (await GetView(owner, room.Id))!.Stats);
    }

    [Fact]
    public async Task ListTrendingReflectsRecentVotesAndExcludesResolved()
    {
        var owner = await NewUser();
        var voter = await NewUser();
        var room = await CreateRoom(owner);
        var q1 = await For(owner).Questions.Post(new Questions_Post(room.Id, "Trending?"));
        var q2 = await For(owner).Questions.Post(new Questions_Post(room.Id, "Gone?"));
        await For(voter).Questions.Vote(new Questions_Vote(room.Id, q1.Index, true));
        await For(voter).Questions.Vote(new Questions_Vote(room.Id, q2.Index, true));
        await For(owner).Questions.Resolve(new Questions_Resolve(room.Id, q2.Index, ""));
        Assert.Equal(
            new[] { new TrendingQuestion(q1.Index, "Trending?", 1) },
            await GetTrending(owner, room.Id, 10));
    }
}

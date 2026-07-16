namespace TownHall.Tests;

public abstract class RoomStatsTests(TestAppHost host) : TestBase(host)
{
    [Fact]
    public async Task GetStatsCounts()
    {
        var owner = await NewUser();
        var voter = await NewUser();
        var room = await CreateRoom(owner);
        var q1 = await Call(new Questions_Post(owner, room.Id, "Open?"));
        var q2 = await Call(new Questions_Post(owner, room.Id, "Resolved?"));
        await Call(new Questions_Vote(voter, room.Id, q1.Index, true));
        await Call(new Questions_Vote(owner, room.Id, q2.Index, true));
        await Call(new Questions_Resolve(owner, room.Id, q2.Index, ""));
        await Call(new Presence_Watch(voter, room.Id));
        Assert.Equal(new RoomStats(1, 1, 2, 1), await RoomStats.GetStats(owner, room.Id));
    }

    [Fact]
    public async Task ListTrendingReflectsRecentVotesAndExcludesResolved()
    {
        var owner = await NewUser();
        var voter = await NewUser();
        var room = await CreateRoom(owner);
        var q1 = await Call(new Questions_Post(owner, room.Id, "Trending?"));
        var q2 = await Call(new Questions_Post(owner, room.Id, "Gone?"));
        await Call(new Questions_Vote(voter, room.Id, q1.Index, true));
        await Call(new Questions_Vote(voter, room.Id, q2.Index, true));
        await Call(new Questions_Resolve(owner, room.Id, q2.Index, ""));
        Assert.Equal(
            new[] { new TrendingQuestion(room.Id, q1.Index, 1) },
            await RoomStats.ListTrending(owner, room.Id, 10));
    }
}

public sealed class RoomStatsServerTests(TestAppHost host) : RoomStatsTests(host)
{
    protected override IServiceProvider TestServices => Host.Services;
}

public sealed class RoomStatsClientTests(TestAppHost host) : RoomStatsTests(host)
{
    protected override IServiceProvider TestServices => Host.ClientServices;
}

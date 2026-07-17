namespace TownHall.Tests;

public abstract class MoodTests(TestAppHost host) : TestBase(host)
{
    [Fact]
    public async Task SetMoodRequiresLiveRoomAndValidLevel()
    {
        var owner = await NewUser();
        var room = await CreateRoom(owner, live: false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Call(new Mood_Set(owner, room.Id, 3)));
        await Call(new Rooms_SetLive(owner, room.Id, true));
        await Assert.ThrowsAsync<ArgumentException>(() => Call(new Mood_Set(owner, room.Id, 0)));
        await Assert.ThrowsAsync<ArgumentException>(() => Call(new Mood_Set(owner, room.Id, 6)));
    }

    [Fact]
    public async Task GetOwnReturnsStoredLevel()
    {
        var owner = await NewUser();
        var room = await CreateRoom(owner);
        Assert.Null(await Mood.GetOwn(owner, room.Id));
        await Call(new Mood_Set(owner, room.Id, 4));
        Assert.Equal(4, await ReadWhen(() => Mood.GetOwn(owner, room.Id), l => l == 4));
        await Call(new Mood_Set(owner, room.Id, 2));
        Assert.Equal(2, await ReadWhen(() => Mood.GetOwn(owner, room.Id), l => l == 2));
    }

    [Fact]
    public async Task GetSummaryCountsOnlyPresentSessions()
    {
        var owner = await NewUser();
        var other = await NewUser();
        var room = await CreateRoom(owner);
        await Call(new Mood_Set(owner, room.Id, 3));
        await Call(new Mood_Set(other, room.Id, 5));
        Assert.Equal(0, (await Mood.GetSummary(owner, room.Id)).VoterCount);
        await Call(new Presence_Watch(owner, room.Id));
        await Call(new Presence_Watch(other, room.Id));
        var summary = await ReadWhen(() => Mood.GetSummary(owner, room.Id), s => s.VoterCount == 2);
        Assert.Equal(4.0, summary.Average);
        Assert.Equal(new[] { 0, 0, 1, 0, 1 }, summary.Counts);
    }
}

public sealed class MoodServerTests(TestAppHost host) : MoodTests(host)
{
    protected override IServiceProvider TestServices => Host.Services;
}

public sealed class MoodClientTests(TestAppHost host) : MoodTests(host)
{
    protected override IServiceProvider TestServices => Host.ClientServices;
}

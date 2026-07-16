namespace TownHall.Tests;

public abstract class PresenceTests(TestAppHost host) : TestBase(host)
{
    [Fact]
    public async Task GetAudienceCountTracksWatchers()
    {
        var owner = await NewUser();
        var watcher1 = await NewUser();
        var watcher2 = await NewUser();
        var room = await CreateRoom(owner, live: false);
        Assert.Equal(0, await Presence.GetAudienceCount(owner, room.Id));
        await Call(new Presence_Watch(watcher1, room.Id));
        Assert.Equal(1, await ReadWhen(() => Presence.GetAudienceCount(owner, room.Id), c => c == 1));
        await Call(new Presence_Watch(watcher2, room.Id));
        await Call(new Presence_Watch(watcher1, room.Id)); // A repeated heartbeat isn't double-counted
        Assert.Equal(2, await ReadWhen(() => Presence.GetAudienceCount(owner, room.Id), c => c == 2));
    }
}

public sealed class PresenceServerTests(TestAppHost host) : PresenceTests(host)
{
    protected override IServiceProvider TestServices => Host.Services;
}

public sealed class PresenceClientTests(TestAppHost host) : PresenceTests(host)
{
    protected override IServiceProvider TestServices => Host.ClientServices;
}

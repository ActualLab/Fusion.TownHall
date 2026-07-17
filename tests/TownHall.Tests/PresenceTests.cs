namespace TownHall.Tests;

public sealed class PresenceTests(TestAppHost host) : TestBase(host)
{
    [Fact]
    public async Task AudienceCountTracksWatchers()
    {
        var owner = await NewUser();
        var watcher1 = await NewUser();
        var watcher2 = await NewUser();
        var room = await CreateRoom(owner, live: false);
        Assert.Equal(0, await GetAudience(owner, room.Id));
        await For(watcher1).Presence.Watch(new Presence_Watch(room.Id));
        Assert.Equal(1, await GetAudience(owner, room.Id));
        await For(watcher2).Presence.Watch(new Presence_Watch(room.Id));
        await For(watcher1).Presence.Watch(new Presence_Watch(room.Id)); // A repeated heartbeat isn't double-counted
        Assert.Equal(2, await GetAudience(owner, room.Id));
    }
}

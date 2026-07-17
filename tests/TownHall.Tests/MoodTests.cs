namespace TownHall.Tests;

public sealed class MoodTests(TestAppHost host) : TestBase(host)
{
    [Fact]
    public async Task SetMoodRequiresLiveRoomAndValidLevel()
    {
        var owner = await NewUser();
        var room = await CreateRoom(owner, live: false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => For(owner).Mood.SetMood(new Mood_Set(room.Id, 3)));
        await For(owner).Rooms.SetLive(new Rooms_SetLive(room.Id, true));
        await Assert.ThrowsAsync<ArgumentException>(() => For(owner).Mood.SetMood(new Mood_Set(room.Id, 0)));
        await Assert.ThrowsAsync<ArgumentException>(() => For(owner).Mood.SetMood(new Mood_Set(room.Id, 6)));
    }

    [Fact]
    public async Task GetOwnReturnsStoredLevel()
    {
        var owner = await NewUser();
        var room = await CreateRoom(owner);
        Assert.Null((await GetMood(owner, room.Id)).OwnLevel);
        await For(owner).Mood.SetMood(new Mood_Set(room.Id, 4));
        Assert.Equal(4, (await GetMood(owner, room.Id)).OwnLevel);
        await For(owner).Mood.SetMood(new Mood_Set(room.Id, 2));
        Assert.Equal(2, (await GetMood(owner, room.Id)).OwnLevel);
    }

    [Fact]
    public async Task GetSummaryCountsOnlyPresentSessions()
    {
        var owner = await NewUser();
        var other = await NewUser();
        var room = await CreateRoom(owner);
        await For(owner).Mood.SetMood(new Mood_Set(room.Id, 3));
        await For(other).Mood.SetMood(new Mood_Set(room.Id, 5));
        Assert.Equal(0, (await GetMood(owner, room.Id)).Summary.VoterCount);
        await For(owner).Presence.Watch(new Presence_Watch(room.Id));
        await For(other).Presence.Watch(new Presence_Watch(room.Id));
        var summary = (await GetMood(owner, room.Id)).Summary;
        Assert.Equal(2, summary.VoterCount);
        Assert.Equal(4.0, summary.Average);
        Assert.Equal(new[] { 0, 0, 1, 0, 1 }, summary.Counts);
    }
}

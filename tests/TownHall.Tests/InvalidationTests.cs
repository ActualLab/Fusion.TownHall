namespace TownHall.Tests;

public sealed class InvalidationTests(TestAppHost host) : TestBase(host)
{
    protected override IServiceProvider TestServices => Host.Services;

    [Fact]
    public async Task ListOpenIdsIsInvalidatedOnPost()
    {
        var owner = Session.New();
        var room = await CreateRoom(owner);
        var computed = await Computed.Capture(() => Questions.ListOpenIds(owner, room.Id));
        Assert.Empty(computed.Value);
        await Call(new Questions_Post(owner, room.Id, "Invalidate me?"));
        await computed.WhenInvalidated().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(await Questions.ListOpenIds(owner, room.Id));
    }
}

namespace TownHall.Tests;

public sealed class InvalidationTests(TestAppHost host) : TestBase(host)
{
    protected override IServiceProvider TestServices => Host.Services;

    [Fact]
    public async Task ListOpenIsInvalidatedOnPost()
    {
        var owner = await NewUser();
        var room = await CreateRoom(owner);
        var computed = await Computed.Capture(() => Questions.ListOpen(owner, room.Id));
        Assert.Empty(computed.Value);
        await Call(new Questions_Post(owner, room.Id, "Invalidate me?"));
        await computed.WhenInvalidated().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(await Questions.ListOpen(owner, room.Id));
    }

    [Fact]
    public async Task ListOpenInvalidationDelayIsAppliedOnceForClient()
    {
        // ListOpen carries [ComputeMethod(InvalidationDelay = 0.5)]. Fusion applies that delay to the
        // server-side computed only; a client replica reads its delay from ReplicaMethodAttribute
        // (absent here), so it must not re-apply it. A client observer should therefore see the
        // invalidation after ~0.5s, not ~1s (which is what a doubled delay would produce).
        const double delaySeconds = 0.5;
        var client = Host.ClientServices;
        var clientQuestions = client.GetRequiredService<IQuestions>();
        var clientCommander = client.Commander();

        var owner = await NewUser();
        var room = await clientCommander.Call(new Rooms_Create(owner, "Delay Test", TimeSpan.FromHours(1)));
        await clientCommander.Call(new Rooms_SetLive(owner, room.Id, true));

        var computed = await Computed.Capture(() => clientQuestions.ListOpen(owner, room.Id));
        Assert.Empty(computed.Value);

        await clientCommander.Call(new Questions_Post(owner, room.Id, "Delayed?"));
        var postedAt = CpuTimestamp.Now;
        await computed.WhenInvalidated().WaitAsync(WaitTimeout);
        var elapsed = postedAt.Elapsed;

        // Applied once: clearly above 0 and clearly below the ~1s a doubled delay would yield.
        Assert.InRange(elapsed.TotalSeconds, delaySeconds * 0.6, delaySeconds * 1.6);
    }

    [Fact]
    public async Task GetNameIsInvalidatedOnRename()
    {
        var owner = await NewUser();
        var room = await CreateRoom(owner);
        var q = await Call(new Questions_Post(owner, room.Id, "Whose question?"));
        var computed = await Computed.Capture(() => Users.Get(owner, q.AuthorId));
        await Call(new Users_SetName(owner, "Renamed Author"));
        await computed.WhenInvalidated().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Renamed Author", (await Users.Get(owner, q.AuthorId))!.Name);
    }
}

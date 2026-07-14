namespace TownHall.Tests;

public abstract class ParticipantsTests(TestAppHost host) : TestBase(host)
{
    [Fact]
    public async Task GetOwnReturnsStableGeneratedName()
    {
        var session = Session.New();
        var info = await Participants.GetOwn(session);
        Assert.Matches(@"^\w+ \w+$", info.Name);
        Assert.Equal(info, await Participants.GetOwn(session));
    }

    [Fact]
    public async Task OnSetNameChangesName()
    {
        var session = Session.New();
        _ = await Participants.GetOwn(session);
        await Call(new Participants_SetName(session, "  Alice  "));
        var info = await ReadWhen(() => Participants.GetOwn(session), i => i.Name == "Alice");
        Assert.Equal("Alice", info.Name);
        await Assert.ThrowsAsync<ArgumentException>(
            () => Call(new Participants_SetName(session, "   ")));
        await Assert.ThrowsAsync<ArgumentException>(
            () => Call(new Participants_SetName(session, new string('x', 31))));
    }
}

public sealed class ParticipantsServerTests(TestAppHost host) : ParticipantsTests(host)
{
    protected override IServiceProvider TestServices => Host.Services;
}

public sealed class ParticipantsClientTests(TestAppHost host) : ParticipantsTests(host)
{
    protected override IServiceProvider TestServices => Host.ClientServices;
}

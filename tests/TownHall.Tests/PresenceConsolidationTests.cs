using TownHall.Host.Services;

namespace TownHall.Tests;

// GetPresentUsers carries [ComputeMethod(ConsolidationDelay = 0.1)] over GetPresentUsersRaw, which
// self-invalidates every Ttl to detect expiry. These tests pin the payoff: a re-check that yields the same
// present set must be swallowed (so an occupied-but-idle room doesn't churn presence-dependent reads), while
// a real membership change must still propagate.
public sealed class PresenceConsolidationTests(TestAppHost host) : TestBase(host)
{
    protected override IServiceProvider TestServices => Host.Services;

    [Fact]
    public async Task UnchangedPresenceReCheckDoesNotInvalidateDependents()
    {
        var presence = (PresenceBackend)Host.Services.GetRequiredService<IPresenceBackend>();
        var owner = await NewUser();
        var watcher = await NewUser();
        var room = await CreateRoom(owner);
        await Call(new Presence_Watch(watcher, room.Id));

        // Capture a presence-dependent read once it reflects the watcher
        using var cts = new CancellationTokenSource(WaitTimeout);
        var audience = await Computed.Capture(() => Presence.GetAudienceCount(owner, room.Id), cts.Token);
        audience = await audience.When(c => c == 1, cts.Token);

        // The Ttl re-check with an unchanged set: invalidating the raw read must be swallowed by
        // consolidation, so the dependent stays consistent (> ConsolidationDelay must pass with no invalidation)
        using (Invalidation.Begin())
            _ = presence.GetPresentUsersRaw(room.Id);
        await Assert.ThrowsAsync<TimeoutException>(
            () => audience.WhenInvalidated().WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.True(audience.IsConsistent());

        // A real change (a second watcher joins) must still invalidate the dependent
        await Call(new Presence_Watch(await NewUser(), room.Id));
        await audience.WhenInvalidated().WaitAsync(WaitTimeout);
        Assert.Equal(2, await Presence.GetAudienceCount(owner, room.Id));
    }
}

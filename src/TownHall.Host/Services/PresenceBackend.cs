namespace TownHall.Host.Services;

/// <summary>
/// Tracks per-room presence heartbeats in memory: a user counts as present while its last
/// <see cref="OnWatch"/> is within <see cref="Ttl"/>. Nothing is ever written to the DB - presence
/// is host-local, ephemeral state. Only signed-in users are present (the frontend rejects guests).
/// </summary>
public class PresenceBackend(IServiceProvider services) : IPresenceBackend
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<(string RoomId, string UserId), Moment> _lastSeen = new();

    private MomentClockSet Clocks { get; } = services.Clocks();

    public virtual async Task<int> GetAudienceCount(string roomId, CancellationToken cancellationToken = default)
        => (await GetPresentUsers(roomId).ConfigureAwait(false)).Count;

    // ConsolidationDelay = 0.1: the raw read below is invalidated on every heartbeat-expiry re-check, but a
    // re-check that yields the same set is swallowed here (PresentUsers has value equality), so a steady
    // room full of heartbeats never invalidates the stats/mood reads that depend on this. The 0.1s window
    // also coalesces bursts of joins/leaves into a single downstream update.
    [ComputeMethod(ConsolidationDelay = 0.1)]
    public virtual async Task<PresentUsers> GetPresentUsers(string roomId)
        => await GetPresentUsersRaw(roomId).ConfigureAwait(false);

    [ComputeMethod]
    public virtual Task<PresentUsers> GetPresentUsersRaw(string roomId)
    {
        var now = Clocks.SystemClock.Now;
        var minLastSeen = now - Ttl;
        var present = new List<KeyValuePair<(string RoomId, string UserId), Moment>>();
        foreach (var kv in _lastSeen) {
            if (!string.Equals(kv.Key.RoomId, roomId, StringComparison.Ordinal))
                continue;

            if (kv.Value >= minLastSeen)
                present.Add(kv);
            else
                _lastSeen.TryRemove(kv); // Expired long ago; drop to keep the map bounded
        }
        if (present.Count == 0)
            return Task.FromResult(PresentUsers.Empty);

        // Re-evaluate when the earliest-seen user is about to expire
        Computed.GetCurrent().Invalidate(present.Min(kv => kv.Value) + Ttl - now + TimeSpan.FromMilliseconds(100));
        return Task.FromResult(new PresentUsers([..present.Select(kv => kv.Key.UserId).Order(StringComparer.Ordinal)]));
    }

    public virtual Task OnWatch(string roomId, string userId)
    {
        var now = Clocks.SystemClock.Now;
        var key = (roomId, userId);
        var wasPresent = _lastSeen.TryGetValue(key, out var lastSeen) && lastSeen >= now - Ttl;
        _lastSeen[key] = now;
        // A repeated heartbeat only shifts expiry; a join changes the set, so invalidate the raw read.
        // GetPresentUsers consolidates on top of it, so this - and the raw's every-Ttl self-invalidation -
        // only reach dependents when the present set actually changes.
        if (!wasPresent) {
            using var invalidating = Invalidation.Begin();
            _ = GetPresentUsersRaw(roomId);
        }
        return Task.CompletedTask;
    }
}

namespace TownHall.Host.Services;

/// <summary>
/// Tracks per-room presence heartbeats in memory: a session counts as present
/// while its last <see cref="Presence_Watch"/> is within <see cref="Ttl"/>.
/// </summary>
public class PresenceService(IServiceProvider services) : IPresence
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<(string RoomId, string SessionId), Moment> _lastSeen = new();

    private MomentClockSet Clocks { get; } = services.Clocks();

    public virtual async Task<int> GetAudienceCount(Session session, string roomId, CancellationToken cancellationToken = default)
        => (await GetPresentSessionIds(roomId).ConfigureAwait(false)).Length;

    public virtual Task OnWatch(Presence_Watch command, CancellationToken cancellationToken = default)
    {
        var (session, roomId) = command;
        var now = Clocks.SystemClock.Now;
        var key = (roomId, session.Id);
        var wasPresent = _lastSeen.TryGetValue(key, out var lastSeen) && lastSeen >= now - Ttl;
        _lastSeen[key] = now;
        // A repeated heartbeat only shifts expiry, which the scheduled
        // invalidation in GetPresentSessionIds re-reads anyway
        if (!wasPresent) {
            using var invalidating = Invalidation.Begin();
            _ = GetPresentSessionIds(roomId);
        }
        return Task.CompletedTask;
    }

    // Protected/internal methods

    // Public (though not part of IPresence), so mood/stats services can compose on top of it
    [ComputeMethod]
    public virtual Task<string[]> GetPresentSessionIds(string roomId)
    {
        var now = Clocks.SystemClock.Now;
        var minLastSeen = now - Ttl;
        var present = new List<KeyValuePair<(string RoomId, string SessionId), Moment>>();
        foreach (var kv in _lastSeen) {
            if (!string.Equals(kv.Key.RoomId, roomId, StringComparison.Ordinal))
                continue;

            if (kv.Value >= minLastSeen)
                present.Add(kv);
            else
                _lastSeen.TryRemove(kv); // Expired long ago; drop to keep the map bounded
        }
        // Re-evaluate when the earliest-seen session is about to expire
        if (present.Count != 0)
            Computed.GetCurrent().Invalidate(present.Min(kv => kv.Value) + Ttl - now + TimeSpan.FromMilliseconds(100));
        return Task.FromResult(present.Select(kv => kv.Key.SessionId).Order().ToArray());
    }
}

namespace TownHall.Host.Services;

// The set of sessions present in a room, with value (sequence) equality. This lets GetPresentSessions
// use ConsolidationDelay: the every-Ttl re-check recomputes the set from memory, and when it's unchanged
// the invalidation is swallowed - so a steady room full of heartbeats never re-hits the DB downstream.
public sealed class PresentSessions(string[] sessionIds) : IEquatable<PresentSessions>
{
    public static readonly PresentSessions Empty = new([]);

    public string[] SessionIds { get; } = sessionIds;
    public int Count => SessionIds.Length;

    public bool Contains(string sessionId) => Array.IndexOf(SessionIds, sessionId) >= 0;

    public bool Equals(PresentSessions? other)
        => other is not null && SessionIds.AsSpan().SequenceEqual(other.SessionIds);
    public override bool Equals(object? obj) => Equals(obj as PresentSessions);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var id in SessionIds)
            hash.Add(id, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Tracks per-room presence heartbeats in memory: a session counts as present
/// while its last <see cref="Presence_Watch"/> is within <see cref="Ttl"/>. Nothing
/// is ever written to the DB - presence is host-local, ephemeral state.
/// </summary>
public class PresenceService(IServiceProvider services) : IPresence
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<(string RoomId, string SessionId), Moment> _lastSeen = new();

    private MomentClockSet Clocks { get; } = services.Clocks();

    public virtual async Task<int> GetAudienceCount(Session session, string roomId, CancellationToken cancellationToken = default)
        => (await GetPresentSessions(roomId).ConfigureAwait(false)).Count;

    public virtual Task OnWatch(Presence_Watch command, CancellationToken cancellationToken = default)
    {
        var (session, roomId) = command;
        var now = Clocks.SystemClock.Now;
        var key = (roomId, session.Id);
        var wasPresent = _lastSeen.TryGetValue(key, out var lastSeen) && lastSeen >= now - Ttl;
        _lastSeen[key] = now;
        // A repeated heartbeat only shifts expiry; a join changes the set, so invalidate the raw read.
        // GetPresentSessions consolidates on top of it, so this - and the raw's every-Ttl self-invalidation -
        // only reach dependents when the present set actually changes.
        if (!wasPresent) {
            using var invalidating = Invalidation.Begin();
            _ = GetPresentSessionsRaw(roomId);
        }
        return Task.CompletedTask;
    }

    // Protected/internal methods

    // Public (though not part of IPresence), so mood/stats services can compose on top of it.
    // ConsolidationDelay = 0.1: the raw read below is invalidated on every heartbeat-expiry re-check, but a
    // re-check that yields the same set is swallowed here (PresentSessions has value equality), so a steady
    // room full of heartbeats never invalidates the stats/mood reads that depend on this. The 0.1s window
    // also coalesces bursts of joins/leaves into a single downstream update.
    [ComputeMethod(ConsolidationDelay = 0.1)]
    public virtual async Task<PresentSessions> GetPresentSessions(string roomId)
        => await GetPresentSessionsRaw(roomId).ConfigureAwait(false);

    // Public (not part of IPresence) so tests can invalidate it to exercise consolidation directly
    [ComputeMethod]
    public virtual Task<PresentSessions> GetPresentSessionsRaw(string roomId)
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
        if (present.Count == 0)
            return Task.FromResult(PresentSessions.Empty);

        // Re-evaluate when the earliest-seen session is about to expire
        Computed.GetCurrent().Invalidate(present.Min(kv => kv.Value) + Ttl - now + TimeSpan.FromMilliseconds(100));
        return Task.FromResult(new PresentSessions([..present.Select(kv => kv.Key.SessionId).Order(StringComparer.Ordinal)]));
    }
}

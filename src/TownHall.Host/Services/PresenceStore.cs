namespace TownHall.Host.Services;

/// <summary>
/// Tracks per-room presence heartbeats in memory (shared across all connections): a session counts as
/// present while its last <see cref="Watch"/> is within <see cref="Ttl"/>.
/// </summary>
public sealed class PresenceStore
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<(string RoomId, string SessionId), Moment> _lastSeen = new();

    // Records a heartbeat; returns true if this made the room's present-set change (a fresh arrival),
    // which is when the caller should notify watchers.
    public bool Watch(string roomId, string sessionId)
    {
        var now = Moment.Now;
        var key = (roomId, sessionId);
        var wasPresent = _lastSeen.TryGetValue(key, out var lastSeen) && lastSeen >= now - Ttl;
        _lastSeen[key] = now;
        return !wasPresent;
    }

    // Present session ids (sorted), plus the moment the earliest-seen one expires - so a caller can
    // schedule a self-wake to reflect it dropping off even if no one else heartbeats.
    public (ImmutableArray<string> Ids, Moment? NextExpiry) Present(string roomId)
    {
        var now = Moment.Now;
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
        var nextExpiry = present.Count == 0 ? (Moment?)null : present.Min(kv => kv.Value) + Ttl;
        return ([..present.Select(kv => kv.Key.SessionId).Order()], nextExpiry);
    }

    public int Count(string roomId)
        => Present(roomId).Ids.Length;
}

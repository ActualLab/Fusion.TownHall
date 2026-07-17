namespace TownHall;

// Backend presence tracker (host-local, in-memory - never touches the DB). Keyed by user id: only
// signed-in users are present, so the audience is the set of present users. Watch is a plain
// memory-only heartbeat that notifies watchers only when the present-set actually changes.
public interface IPresenceBackend
{
    void Watch(PresenceBackend_Watch command);
}

// ReSharper disable once InconsistentNaming
public sealed record PresenceBackend_Watch(
    string RoomId,
    string UserId
);

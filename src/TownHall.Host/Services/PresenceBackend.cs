namespace TownHall.Host.Services;

// Backend presence: records a heartbeat for a user in a room (in-memory PresenceStore, keyed by user
// id - only signed-in users are present) and notifies watchers when the present-set actually changes.
public sealed class PresenceBackend(PresenceStore presence, ChangeTracker changes) : IPresenceBackend
{
    public void Watch(PresenceBackend_Watch command)
    {
        var (roomId, userId) = command;
        if (presence.Watch(roomId, userId))
            changes.Notify($"room:{roomId}");
    }
}

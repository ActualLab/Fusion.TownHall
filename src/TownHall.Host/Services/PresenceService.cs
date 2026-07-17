namespace TownHall.Host.Services;

// Frontend presence service: only signed-in users report presence; a guest heartbeat is a no-op.
public sealed class PresenceService(ServerShared shared, Identity identity)
    : ServerService(shared, identity), IPresence
{
    public async Task Watch(Presence_Watch command, CancellationToken cancellationToken = default)
    {
        var userId = await GetUserId(cancellationToken).ConfigureAwait(false);
        if (userId == null)
            return; // Guests don't report presence

        Shared.PresenceBackend.Watch(new PresenceBackend_Watch(command.RoomId, userId));
    }
}

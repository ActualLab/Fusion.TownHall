namespace TownHall.Host.Services;

// Frontend presence service: only signed-in users report presence; a guest heartbeat is a no-op.
public class PresenceService(IServiceProvider services) : IPresence
{
    private IPresenceBackend Backend => field ??= services.GetRequiredService<IPresenceBackend>();
    private IUsersBackend Users => field ??= services.GetRequiredService<IUsersBackend>();

    public virtual Task<int> GetAudienceCount(Session session, string roomId, CancellationToken cancellationToken = default)
        => Backend.GetAudienceCount(roomId, cancellationToken);

    public virtual async Task OnWatch(Presence_Watch command, CancellationToken cancellationToken = default)
    {
        var (session, roomId) = command;
        var userId = await Users.GetUserIdBySession(session.Id, cancellationToken).ConfigureAwait(false);
        if (userId == null)
            return; // Guests don't report presence

        await Backend.OnWatch(roomId, userId).ConfigureAwait(false);
    }
}

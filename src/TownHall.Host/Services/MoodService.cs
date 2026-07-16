namespace TownHall.Host.Services;

// Frontend mood service: the summary is open to guests; a guest has no own mood and can't set one.
public class MoodService(IServiceProvider services) : IMood
{
    private IMoodBackend Backend => field ??= services.GetRequiredService<IMoodBackend>();
    private IUsersBackend Users => field ??= services.GetRequiredService<IUsersBackend>();
    private ICommander Commander => field ??= services.Commander();

    public virtual Task<MoodSummary> GetSummary(Session session, string roomId, CancellationToken cancellationToken = default)
        => Backend.GetSummary(roomId, cancellationToken);

    public virtual async Task<int?> GetOwn(Session session, string roomId, CancellationToken cancellationToken = default)
    {
        var userId = await Users.GetUserIdBySession(session.Id, cancellationToken).ConfigureAwait(false);
        return userId == null
            ? null
            : await Backend.GetOwn(roomId, userId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnSetMood(Mood_Set command, CancellationToken cancellationToken = default)
    {
        var (session, roomId, level) = command;
        var userId = (await Users.GetUserIdBySession(session.Id, cancellationToken).ConfigureAwait(false)).RequireSignedIn();
        await Commander.Call(new MoodBackend_Set(roomId, userId, level), true, cancellationToken).ConfigureAwait(false);
    }
}

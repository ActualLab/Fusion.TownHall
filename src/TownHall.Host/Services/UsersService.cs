namespace TownHall.Host.Services;

// Frontend user service: maps a Session to its (possibly guest) user via IUsersBackend and delegates.
public class UsersService(IServiceProvider services) : IUsers
{
    private IUsersBackend Backend => field ??= services.GetRequiredService<IUsersBackend>();
    private ICommander Commander => field ??= services.Commander();

    public virtual Task<string?> GetOwnUserId(Session session, CancellationToken cancellationToken = default)
        => Backend.GetUserIdBySession(session.Id, cancellationToken);

    public virtual async Task<UserFull?> GetOwn(Session session, CancellationToken cancellationToken = default)
    {
        var userId = await GetOwnUserId(session, cancellationToken).ConfigureAwait(false);
        return userId == null
            ? null
            : await Backend.Get(userId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<User?> Get(Session session, string userId, CancellationToken cancellationToken = default)
    {
        var user = await Backend.Get(userId, cancellationToken).ConfigureAwait(false);
        return user?.ToUser();
    }

    public virtual async Task OnSetName(Users_SetName command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return;

        var (session, name) = command;
        var userId = (await GetOwnUserId(session, cancellationToken).ConfigureAwait(false)).RequireSignedIn();
        await Commander.Call(new UsersBackend_SetName(userId, name), true, cancellationToken).ConfigureAwait(false);
    }
}

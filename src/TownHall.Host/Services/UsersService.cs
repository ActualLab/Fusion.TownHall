namespace TownHall.Host.Services;

// Frontend user service: streams this session's own account (null while a guest) and renames it.
public sealed class UsersService(ServerShared shared, Identity identity)
    : ServerService(shared, identity), IUsers
{
    public IAsyncEnumerable<UserFull?> GetOwn(CancellationToken cancellationToken = default)
        => Stream(SessionScope, async ct => {
            var userId = await GetUserId(ct).ConfigureAwait(false);
            var user = userId != null ? await Shared.Users.Get(userId, ct).ConfigureAwait(false) : null;
            return (user, (TimeSpan?)null);
        }, cancellationToken);

    public async Task SetName(Users_SetName command, CancellationToken cancellationToken = default)
    {
        var userId = await RequireUserId(cancellationToken).ConfigureAwait(false);
        await Shared.Users.SetName(new UsersBackend_SetName(userId, command.Name), cancellationToken).ConfigureAwait(false);
    }
}

namespace TownHall.Host.Services;

// Frontend room service: resolves the caller, enforces sign-in / ownership, then delegates to
// IRoomsBackend. Reads are open to guests; writes require sign-in and (where noted) ownership.
public class RoomsService(IServiceProvider services) : IRooms
{
    private IRoomsBackend Backend => field ??= services.GetRequiredService<IRoomsBackend>();
    private IUsersBackend Users => field ??= services.GetRequiredService<IUsersBackend>();
    private ICommander Commander => field ??= services.Commander();

    public virtual Task<Room?> Get(Session session, string roomId, CancellationToken cancellationToken = default)
        => Backend.Get(roomId, cancellationToken);

    public virtual async Task<ImmutableArray<string>> ListRooms(Session session, int limit, CancellationToken cancellationToken = default)
    {
        var ids = await Backend.ListRoomIds(cancellationToken).ConfigureAwait(false);
        return limit < ids.Length ? ids[..limit] : ids;
    }

    public virtual async Task<bool> IsOwner(Session session, string roomId, CancellationToken cancellationToken = default)
    {
        var userId = await Users.GetUserIdBySession(session.Id, cancellationToken).ConfigureAwait(false);
        return userId != null && await Backend.IsOwner(roomId, userId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<string?> GetOwnerToken(Session session, string roomId, CancellationToken cancellationToken = default)
        => await IsOwner(session, roomId, cancellationToken).ConfigureAwait(false)
            ? await Backend.GetOwnerToken(roomId, cancellationToken).ConfigureAwait(false)
            : null;

    public virtual async Task<Room> OnCreate(Rooms_Create command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return null!;

        var (session, title, duration, isPrivate, link, description) = command;
        var userId = (await GetOwnUserId(session, cancellationToken).ConfigureAwait(false)).RequireSignedIn();
        return await Commander
            .Call(new RoomsBackend_Create(userId, title, duration, isPrivate, link, description), true, cancellationToken)
            .ConfigureAwait(false);
    }

    public virtual async Task OnClaimOwnership(Rooms_ClaimOwnership command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return;

        var (session, roomId, ownerToken) = command;
        var userId = (await GetOwnUserId(session, cancellationToken).ConfigureAwait(false)).RequireSignedIn();
        await Commander.Call(new RoomsBackend_ClaimOwnership(roomId, userId, ownerToken), true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnSetLive(Rooms_SetLive command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return;

        var (session, roomId, live) = command;
        await RequireOwner(session, roomId, cancellationToken).ConfigureAwait(false);
        await Commander.Call(new RoomsBackend_SetLive(roomId, live), true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnSetIsPrivate(Rooms_SetIsPrivate command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return;

        var (session, roomId, isPrivate) = command;
        await RequireOwner(session, roomId, cancellationToken).ConfigureAwait(false);
        await Commander.Call(new RoomsBackend_SetIsPrivate(roomId, isPrivate), true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnSetTitle(Rooms_SetTitle command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return;

        var (session, roomId, title) = command;
        await RequireOwner(session, roomId, cancellationToken).ConfigureAwait(false);
        await Commander.Call(new RoomsBackend_SetTitle(roomId, title), true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnSetLink(Rooms_SetLink command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return;

        var (session, roomId, link) = command;
        await RequireOwner(session, roomId, cancellationToken).ConfigureAwait(false);
        await Commander.Call(new RoomsBackend_SetLink(roomId, link), true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnSetDescription(Rooms_SetDescription command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return;

        var (session, roomId, description) = command;
        await RequireOwner(session, roomId, cancellationToken).ConfigureAwait(false);
        await Commander.Call(new RoomsBackend_SetDescription(roomId, description), true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnAdjustDuration(Rooms_AdjustDuration command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return;

        var (session, roomId, delta) = command;
        await RequireOwner(session, roomId, cancellationToken).ConfigureAwait(false);
        await Commander.Call(new RoomsBackend_AdjustDuration(roomId, delta), true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private Task<string?> GetOwnUserId(Session session, CancellationToken cancellationToken)
        => Users.GetUserIdBySession(session.Id, cancellationToken);

    private async Task RequireOwner(Session session, string roomId, CancellationToken cancellationToken)
    {
        var userId = (await GetOwnUserId(session, cancellationToken).ConfigureAwait(false)).RequireSignedIn();
        if (!await Backend.IsOwner(roomId, userId, cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Only town hall owners can do this.");
    }
}

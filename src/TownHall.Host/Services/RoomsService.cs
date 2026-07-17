namespace TownHall.Host.Services;

// Frontend room service: resolves the caller, enforces sign-in / ownership, then delegates to
// IRoomsBackend. Reads are open to guests; writes require sign-in and (where noted) ownership.
public sealed class RoomsService(ServerShared shared, Identity identity)
    : ServerService(shared, identity), IRooms
{
    public IAsyncEnumerable<RoomView?> Get(string roomId, CancellationToken cancellationToken = default)
        => Stream([$"room:{roomId}", SessionScope], ct => ReadRoomView(roomId, ct), cancellationToken);

    public IAsyncEnumerable<ImmutableArray<string>> ListRooms(int limit, CancellationToken cancellationToken = default)
        => Stream("lobby", async ct => {
            var (ids, nextChange) = await Shared.Rooms.ListRoomIds(limit, ct).ConfigureAwait(false);
            return (ids, ToWake(nextChange));
        }, cancellationToken);

    public IAsyncEnumerable<LobbyView> GetLobby(int limit, CancellationToken cancellationToken = default)
        => Stream(["lobby", SessionScope], async ct => {
            var (ids, nextChange) = await Shared.Rooms.ListRoomIds(limit, ct).ConfigureAwait(false);
            var isSignedIn = await GetUserId(ct).ConfigureAwait(false) != null;
            return (new LobbyView(ids, isSignedIn), ToWake(nextChange));
        }, cancellationToken);

    public IAsyncEnumerable<RoomCard?> GetCard(string roomId, CancellationToken cancellationToken = default)
        => Stream($"room:{roomId}", async ct => {
            var (card, nextChange) = await Shared.Rooms.ReadRoomCard(roomId, ct).ConfigureAwait(false);
            return (card, ToWake(nextChange));
        }, cancellationToken);

    public async Task<string?> GetOwnerToken(string roomId, CancellationToken cancellationToken = default)
    {
        var userId = await GetUserId(cancellationToken).ConfigureAwait(false);
        return userId != null && await Shared.Rooms.IsOwner(roomId, userId, cancellationToken).ConfigureAwait(false)
            ? await Shared.Rooms.GetOwnerToken(roomId, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async Task<Room> Create(Rooms_Create command, CancellationToken cancellationToken = default)
    {
        var (title, duration, isPrivate, link, description) = command;
        var userId = await RequireUserId(cancellationToken).ConfigureAwait(false);
        return await Shared.Rooms
            .Create(new RoomsBackend_Create(userId, title, duration, isPrivate, link, description), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ClaimOwnership(Rooms_ClaimOwnership command, CancellationToken cancellationToken = default)
    {
        var (roomId, ownerToken) = command;
        var userId = await RequireUserId(cancellationToken).ConfigureAwait(false);
        await Shared.Rooms
            .ClaimOwnership(new RoomsBackend_ClaimOwnership(roomId, userId, ownerToken), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetLive(Rooms_SetLive command, CancellationToken cancellationToken = default)
    {
        var (roomId, live) = command;
        await RequireOwner(roomId, cancellationToken).ConfigureAwait(false);
        await Shared.Rooms.SetLive(new RoomsBackend_SetLive(roomId, live), cancellationToken).ConfigureAwait(false);
    }

    public async Task SetIsPrivate(Rooms_SetIsPrivate command, CancellationToken cancellationToken = default)
    {
        var (roomId, isPrivate) = command;
        await RequireOwner(roomId, cancellationToken).ConfigureAwait(false);
        await Shared.Rooms.SetIsPrivate(new RoomsBackend_SetIsPrivate(roomId, isPrivate), cancellationToken).ConfigureAwait(false);
    }

    public async Task SetTitle(Rooms_SetTitle command, CancellationToken cancellationToken = default)
    {
        var (roomId, title) = command;
        await RequireOwner(roomId, cancellationToken).ConfigureAwait(false);
        await Shared.Rooms.SetTitle(new RoomsBackend_SetTitle(roomId, title), cancellationToken).ConfigureAwait(false);
    }

    public async Task SetLink(Rooms_SetLink command, CancellationToken cancellationToken = default)
    {
        var (roomId, link) = command;
        await RequireOwner(roomId, cancellationToken).ConfigureAwait(false);
        await Shared.Rooms.SetLink(new RoomsBackend_SetLink(roomId, link), cancellationToken).ConfigureAwait(false);
    }

    public async Task SetDescription(Rooms_SetDescription command, CancellationToken cancellationToken = default)
    {
        var (roomId, description) = command;
        await RequireOwner(roomId, cancellationToken).ConfigureAwait(false);
        await Shared.Rooms.SetDescription(new RoomsBackend_SetDescription(roomId, description), cancellationToken).ConfigureAwait(false);
    }

    public async Task AdjustDuration(Rooms_AdjustDuration command, CancellationToken cancellationToken = default)
    {
        var (roomId, delta) = command;
        await RequireOwner(roomId, cancellationToken).ConfigureAwait(false);
        await Shared.Rooms.AdjustDuration(new RoomsBackend_AdjustDuration(roomId, delta), cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<(RoomView? Value, TimeSpan? Wake)> ReadRoomView(string roomId, CancellationToken cancellationToken)
    {
        var (room, roomNextChange) = await Shared.Rooms.ReadRoom(roomId, cancellationToken).ConfigureAwait(false);
        if (room == null)
            return (null, null);

        var userId = await GetUserId(cancellationToken).ConfigureAwait(false);
        var stats = await Shared.Rooms.ReadStats(roomId, cancellationToken).ConfigureAwait(false);
        var isOwner = userId != null && await Shared.Rooms.IsOwner(roomId, userId, cancellationToken).ConfigureAwait(false);
        var anonName = userId != null ? NameGenerator.New(AnonId.Of(userId, roomId)) : "";
        var view = new RoomView(room, isOwner, stats, userId != null, anonName);

        // The audience shrinks as presence expires; re-read when the earliest present user drops off
        var nextExpiry = Presence.Present(roomId).NextExpiry;
        Moment? nextChange = roomNextChange;
        if (nextExpiry is { } e)
            nextChange = nextChange is { } nc ? Moment.Min(nc, e) : e;
        return (view, ToWake(nextChange));
    }

    private async Task RequireOwner(string roomId, CancellationToken cancellationToken)
    {
        var userId = await RequireUserId(cancellationToken).ConfigureAwait(false);
        if (!await Shared.Rooms.IsOwner(roomId, userId, cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Only town hall owners can do this.");
    }
}

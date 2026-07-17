namespace TownHall;

// Backend room store. Permission checks (ownership, sign-in) are the frontend's job; these methods
// assume the caller is already authorized. Argument validation stays here (closest to the data).
// Reads return the value plus the moment it next changes on its own (a status flip or the earliest
// present-user expiry), which the frontend turns into a stream self-wake.
public interface IRoomsBackend
{
    Task<(Room? Room, Moment? NextChange)> ReadRoom(string roomId, CancellationToken cancellationToken = default);
    Task<(ImmutableArray<string> Ids, Moment? NextChange)> ListRoomIds(int limit, CancellationToken cancellationToken = default);
    Task<bool> IsOwner(string roomId, string userId, CancellationToken cancellationToken = default);
    // The room's owner token (unconditional); the frontend only surfaces it to owners.
    Task<string?> GetOwnerToken(string roomId, CancellationToken cancellationToken = default);
    Task<RoomStats> ReadStats(string roomId, CancellationToken cancellationToken = default);
    Task<(RoomCard? Card, Moment? NextChange)> ReadRoomCard(string roomId, CancellationToken cancellationToken = default);

    Task<Room> Create(RoomsBackend_Create command, CancellationToken cancellationToken = default);
    Task ClaimOwnership(RoomsBackend_ClaimOwnership command, CancellationToken cancellationToken = default);
    Task SetLive(RoomsBackend_SetLive command, CancellationToken cancellationToken = default);
    Task SetIsPrivate(RoomsBackend_SetIsPrivate command, CancellationToken cancellationToken = default);
    Task SetTitle(RoomsBackend_SetTitle command, CancellationToken cancellationToken = default);
    Task SetLink(RoomsBackend_SetLink command, CancellationToken cancellationToken = default);
    Task SetDescription(RoomsBackend_SetDescription command, CancellationToken cancellationToken = default);
    Task AdjustDuration(RoomsBackend_AdjustDuration command, CancellationToken cancellationToken = default);
}

// ReSharper disable once InconsistentNaming
public sealed record RoomsBackend_Create(
    string OwnerUserId,
    string Title,
    TimeSpan Duration,
    bool IsPrivate,
    string Link,
    string Description
);

// ReSharper disable once InconsistentNaming
public sealed record RoomsBackend_ClaimOwnership(
    string RoomId,
    string UserId,
    string OwnerToken
);

// ReSharper disable once InconsistentNaming
public sealed record RoomsBackend_SetLive(
    string RoomId,
    bool Live
);

// ReSharper disable once InconsistentNaming
public sealed record RoomsBackend_SetIsPrivate(
    string RoomId,
    bool IsPrivate
);

// ReSharper disable once InconsistentNaming
public sealed record RoomsBackend_SetTitle(
    string RoomId,
    string Title
);

// ReSharper disable once InconsistentNaming
public sealed record RoomsBackend_SetLink(
    string RoomId,
    string Link
);

// ReSharper disable once InconsistentNaming
public sealed record RoomsBackend_SetDescription(
    string RoomId,
    string Description
);

// ReSharper disable once InconsistentNaming
public sealed record RoomsBackend_AdjustDuration(
    string RoomId,
    TimeSpan Delta
);

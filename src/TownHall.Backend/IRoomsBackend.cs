using MessagePack;

namespace TownHall;

// Backend room store. Permission checks (ownership, sign-in) are the frontend's job; these methods
// assume the caller is already authorized. Argument validation stays here (closest to the data).
public interface IRoomsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Room?> Get(string roomId, CancellationToken cancellationToken = default);
    [ComputeMethod]
    Task<ImmutableArray<string>> ListRoomIds(CancellationToken cancellationToken = default);
    [ComputeMethod]
    Task<bool> IsOwner(string roomId, string userId, CancellationToken cancellationToken = default);
    // The room's owner token (unconditional); the frontend only surfaces it to owners.
    [ComputeMethod]
    Task<string?> GetOwnerToken(string roomId, CancellationToken cancellationToken = default);

    [CommandHandler]
    Task<Room> Create(RoomsBackend_Create command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task ClaimOwnership(RoomsBackend_ClaimOwnership command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task SetLive(RoomsBackend_SetLive command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task SetIsPrivate(RoomsBackend_SetIsPrivate command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task SetTitle(RoomsBackend_SetTitle command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task SetLink(RoomsBackend_SetLink command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task SetDescription(RoomsBackend_SetDescription command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task AdjustDuration(RoomsBackend_AdjustDuration command, CancellationToken cancellationToken = default);
}

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record RoomsBackend_Create(
    string OwnerUserId,
    string Title,
    TimeSpan Duration,
    bool IsPrivate,
    string Link,
    string Description
) : ICommand<Room>, IBackendCommand;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record RoomsBackend_ClaimOwnership(
    string RoomId,
    string UserId,
    string OwnerToken
) : ICommand<Unit>, IBackendCommand;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record RoomsBackend_SetLive(
    string RoomId,
    bool Live
) : ICommand<Unit>, IBackendCommand;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record RoomsBackend_SetIsPrivate(
    string RoomId,
    bool IsPrivate
) : ICommand<Unit>, IBackendCommand;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record RoomsBackend_SetTitle(
    string RoomId,
    string Title
) : ICommand<Unit>, IBackendCommand;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record RoomsBackend_SetLink(
    string RoomId,
    string Link
) : ICommand<Unit>, IBackendCommand;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record RoomsBackend_SetDescription(
    string RoomId,
    string Description
) : ICommand<Unit>, IBackendCommand;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record RoomsBackend_AdjustDuration(
    string RoomId,
    TimeSpan Delta
) : ICommand<Unit>, IBackendCommand;

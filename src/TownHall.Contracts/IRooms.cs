using MessagePack;

namespace TownHall;

public interface IRooms : IComputeService
{
    [ComputeMethod]
    Task<Room?> Get(Session session, string roomId, CancellationToken cancellationToken = default);

    // Paused + Live rooms (i.e. not Ended), newest first
    [ComputeMethod]
    Task<ImmutableArray<string>> ListActiveIds(Session session, CancellationToken cancellationToken = default);

    [ComputeMethod]
    Task<bool> IsOwner(Session session, string roomId, CancellationToken cancellationToken = default);

    // The room's owner token - returned ONLY if the session is an owner; null otherwise
    [ComputeMethod]
    Task<string?> GetOwnerToken(Session session, string roomId, CancellationToken cancellationToken = default);

    // Title: trimmed 1..80 chars. Duration is clamped to [5 min, 24 h]; ClosesAt = server now + Duration.
    // Marks the creating session as an owner. The room starts Paused.
    [CommandHandler]
    Task<Room> OnCreate(Rooms_Create command, CancellationToken cancellationToken = default);

    // Validates the token; on match, marks the session as an owner of the room. Idempotent.
    [CommandHandler]
    Task OnClaimOwnership(Rooms_ClaimOwnership command, CancellationToken cancellationToken = default);

    // Owner-only. Live=true => start, Live=false => stop. Idempotent. Rejected once Ended.
    [CommandHandler]
    Task OnSetLive(Rooms_SetLive command, CancellationToken cancellationToken = default);

    // Owner-only. Hides the room from (or returns it to) the public list. Idempotent. Rejected once Ended.
    [CommandHandler]
    Task OnSetIsPrivate(Rooms_SetIsPrivate command, CancellationToken cancellationToken = default);

    // Owner-only. Title: trimmed 1..80 chars. Rejected once Ended.
    [CommandHandler]
    Task OnSetTitle(Rooms_SetTitle command, CancellationToken cancellationToken = default);

    // Owner-only. Shifts ClosesAt by Delta; the result is clamped to [now, CreatedAt + 24 h],
    // so shrinking down to "now" ends the room. An Ended room can be resurrected by a positive
    // Delta within a 10-minute grace period: ClosesAt then becomes now + Delta. Rejected otherwise.
    [CommandHandler]
    Task OnAdjustDuration(Rooms_AdjustDuration command, CancellationToken cancellationToken = default);
}

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Rooms_Create(
    Session Session,
    string Title,
    TimeSpan Duration,
    bool IsPrivate = false
) : ISessionCommand<Room>;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Rooms_ClaimOwnership(
    Session Session,
    string RoomId,
    string OwnerToken
) : ISessionCommand<Unit>;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Rooms_SetLive(
    Session Session,
    string RoomId,
    bool Live
) : ISessionCommand<Unit>;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Rooms_SetIsPrivate(
    Session Session,
    string RoomId,
    bool IsPrivate
) : ISessionCommand<Unit>;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Rooms_SetTitle(
    Session Session,
    string RoomId,
    string Title
) : ISessionCommand<Unit>;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Rooms_AdjustDuration(
    Session Session,
    string RoomId,
    TimeSpan Delta
) : ISessionCommand<Unit>;

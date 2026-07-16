using MessagePack;

namespace TownHall;

public interface IRooms : IComputeService
{
    [ComputeMethod]
    Task<Room?> Get(Session session, string roomId, CancellationToken cancellationToken = default);

    // Public rooms that are active (not Ended) or ended within the last week, newest first, capped at
    // `limit`. Returns just the ordered ids - each list row reads its own RoomStats + mood for live stats.
    [ComputeMethod]
    Task<ImmutableArray<string>> ListRooms(Session session, int limit, CancellationToken cancellationToken = default);

    [ComputeMethod]
    Task<bool> IsOwner(Session session, string roomId, CancellationToken cancellationToken = default);

    // The room's owner token - returned ONLY if the session is an owner; null otherwise
    [ComputeMethod]
    Task<string?> GetOwnerToken(Session session, string roomId, CancellationToken cancellationToken = default);

    // Title: trimmed 1..80 chars. Link: optional http(s) URL (<=500). Description: optional
    // single paragraph (<=1000). Duration is clamped to [5 min, 24 h]. Marks the creating session
    // as an owner. The room starts Paused with the timer frozen at the full duration (it begins
    // counting down only when first resumed).
    [CommandHandler]
    Task<Room> OnCreate(Rooms_Create command, CancellationToken cancellationToken = default);

    // Validates the token; on match, marks the session as an owner of the room. Idempotent.
    [CommandHandler]
    Task OnClaimOwnership(Rooms_ClaimOwnership command, CancellationToken cancellationToken = default);

    // Owner-only. Live=true resumes (shifting EndsAt forward by the paused duration), Live=false
    // pauses (freezing the remaining time). Idempotent. Rejected once Ended.
    [CommandHandler]
    Task OnSetLive(Rooms_SetLive command, CancellationToken cancellationToken = default);

    // Owner-only. Hides the room from (or returns it to) the public list. Idempotent. Rejected once Ended.
    [CommandHandler]
    Task OnSetIsPrivate(Rooms_SetIsPrivate command, CancellationToken cancellationToken = default);

    // Owner-only. Title: trimmed 1..80 chars. Allowed even after Ended (metadata, not votes/questions).
    [CommandHandler]
    Task OnSetTitle(Rooms_SetTitle command, CancellationToken cancellationToken = default);

    // Owner-only. Link: "" or an http(s) URL (<=500). Allowed even after Ended.
    [CommandHandler]
    Task OnSetLink(Rooms_SetLink command, CancellationToken cancellationToken = default);

    // Owner-only. Description: "" or a single paragraph (<=1000). Allowed even after Ended.
    [CommandHandler]
    Task OnSetDescription(Rooms_SetDescription command, CancellationToken cancellationToken = default);

    // Owner-only. Shifts the remaining time by Delta (relative to the room's own clock, frozen while
    // paused), clamped so it stays within [0, 24 h] — shrinking a running hall to 0 ends it. An Ended
    // hall can be resurrected as running by a positive Delta within a 10-minute grace period
    // (EndsAt := now + Delta). Rejected otherwise.
    [CommandHandler]
    Task OnAdjustDuration(Rooms_AdjustDuration command, CancellationToken cancellationToken = default);
}

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Rooms_Create(
    Session Session,
    string Title,
    TimeSpan Duration,
    bool IsPrivate = false,
    string Link = "",
    string Description = ""
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
public sealed record Rooms_SetLink(
    Session Session,
    string RoomId,
    string Link
) : ISessionCommand<Unit>;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Rooms_SetDescription(
    Session Session,
    string RoomId,
    string Description
) : ISessionCommand<Unit>;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Rooms_AdjustDuration(
    Session Session,
    string RoomId,
    TimeSpan Delta
) : ISessionCommand<Unit>;

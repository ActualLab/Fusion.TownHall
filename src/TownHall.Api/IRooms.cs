namespace TownHall;

// Frontend room API. Identity (the current session / user) is implicit: the SignalR connection carries
// it and the hub binds it server-side, so no method takes it. Reads are server-to-client streams;
// actions are plain command records (no Fusion - just records), which keeps them queueable/dedupable.
public interface IRooms
{
    // The room plus whether this session owns it and its live stats; null once the room is gone
    IAsyncEnumerable<RoomView?> Get(string roomId, CancellationToken cancellationToken = default);

    // Public rooms that are active (not Ended) or ended within the last week, newest first, capped at
    // `limit`. Returns just the ordered ids - each list row streams its own RoomCard for live stats.
    IAsyncEnumerable<ImmutableArray<string>> ListRooms(int limit, CancellationToken cancellationToken = default);

    // Like ListRooms, but also carries whether the viewer is signed in (gates the create-room form).
    IAsyncEnumerable<LobbyView> GetLobby(int limit, CancellationToken cancellationToken = default);

    // Live per-row summary: the room plus its audience, total question count, and average mood; null
    // once the room is gone.
    IAsyncEnumerable<RoomCard?> GetCard(string roomId, CancellationToken cancellationToken = default);

    // The room's owner token - returned ONLY if the session is an owner; null otherwise
    Task<string?> GetOwnerToken(string roomId, CancellationToken cancellationToken = default);

    // Title: trimmed 1..80 chars. Link: optional http(s) URL (<=500). Description: optional
    // single paragraph (<=1000). Duration is clamped to [5 min, 24 h]. Marks the creating session
    // as an owner. The room starts Paused with the timer frozen at the full duration (it begins
    // counting down only when first resumed).
    Task<Room> Create(Rooms_Create command, CancellationToken cancellationToken = default);

    // Validates the token; on match, marks the session as an owner of the room. Idempotent.
    Task ClaimOwnership(Rooms_ClaimOwnership command, CancellationToken cancellationToken = default);

    // Owner-only. Live=true resumes (shifting EndsAt forward by the paused duration), Live=false
    // pauses (freezing the remaining time). Idempotent. Rejected once Ended.
    Task SetLive(Rooms_SetLive command, CancellationToken cancellationToken = default);

    // Owner-only. Hides the room from (or returns it to) the public list. Idempotent. Rejected once Ended.
    Task SetIsPrivate(Rooms_SetIsPrivate command, CancellationToken cancellationToken = default);

    // Owner-only. Title: trimmed 1..80 chars. Allowed even after Ended (metadata, not votes/questions).
    Task SetTitle(Rooms_SetTitle command, CancellationToken cancellationToken = default);

    // Owner-only. Link: "" or an http(s) URL (<=500). Allowed even after Ended.
    Task SetLink(Rooms_SetLink command, CancellationToken cancellationToken = default);

    // Owner-only. Description: "" or a single paragraph (<=1000). Allowed even after Ended.
    Task SetDescription(Rooms_SetDescription command, CancellationToken cancellationToken = default);

    // Owner-only. Shifts the remaining time by Delta (relative to the room's own clock, frozen while
    // paused), clamped so it stays within [0, 24 h] — shrinking a running hall to 0 ends it. An Ended
    // hall can be resurrected as running by a positive Delta within a 10-minute grace period
    // (EndsAt := now + Delta). Rejected otherwise.
    Task AdjustDuration(Rooms_AdjustDuration command, CancellationToken cancellationToken = default);
}

// ReSharper disable once InconsistentNaming
public sealed record Rooms_Create(
    string Title,
    TimeSpan Duration,
    bool IsPrivate = false,
    string Link = "",
    string Description = ""
);

// ReSharper disable once InconsistentNaming
public sealed record Rooms_ClaimOwnership(
    string RoomId,
    string OwnerToken
);

// ReSharper disable once InconsistentNaming
public sealed record Rooms_SetLive(
    string RoomId,
    bool Live
);

// ReSharper disable once InconsistentNaming
public sealed record Rooms_SetIsPrivate(
    string RoomId,
    bool IsPrivate
);

// ReSharper disable once InconsistentNaming
public sealed record Rooms_SetTitle(
    string RoomId,
    string Title
);

// ReSharper disable once InconsistentNaming
public sealed record Rooms_SetLink(
    string RoomId,
    string Link
);

// ReSharper disable once InconsistentNaming
public sealed record Rooms_SetDescription(
    string RoomId,
    string Description
);

// ReSharper disable once InconsistentNaming
public sealed record Rooms_AdjustDuration(
    string RoomId,
    TimeSpan Delta
);

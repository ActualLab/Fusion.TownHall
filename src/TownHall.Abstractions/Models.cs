namespace TownHall;

// A public user projection - safe to show to anyone. Guests are represented by null at the
// API boundary; the UI substitutes User.Guest.
public sealed record User(string Id, string Name)
{
    public static readonly User Guest = new("", "Guest");

    public bool IsGuest => Id.Length == 0;
}

// The signed-in user's own account (also the backend's view). Extends User with account-only fields.
public sealed record UserFull(string Id, string Name, Moment CreatedAt)
{
    public User ToUser() => new(Id, Name);
}

public enum RoomStatus
{
    Paused = 0,   // Default at creation; not yet started or temporarily halted (timer frozen)
    Live = 1,
    Ended = 2,    // Running and now >= EndsAt; terminal
}

public sealed record Room(
    string Id,
    string Title,
    // Live-event URL (Zoom/Meet/…), "" if none
    string Link,
    // Single-paragraph blurb, "" if none
    string Description,
    Moment CreatedAt,
    // When the hall ends while running; while paused the remaining time is frozen at EndsAt - PausedAt
    Moment EndsAt,
    // Non-null while Paused (the moment it was paused / created); null while Live
    Moment? PausedAt,
    // Derived at read time; a running hall flips to Ended on its own at EndsAt
    RoomStatus Status,
    // Private rooms are reachable by link, but hidden from IRooms.ListRooms
    bool IsPrivate
);

public enum QuestionStatus { Open = 0, Resolved = 1 }

// Immutable after creation. The mutable facets (vote count, resolution, author name) travel in
// QuestionView, so a single question stream keeps everything the UI shows up to date.
public sealed record Question(
    string RoomId,
    // Per-room, unique, monotonic; gaps possible after deletions
    long Index,
    // Public, stable id of the poster; resolved to a name in QuestionView.AuthorName
    string AuthorId,
    string Text,
    Moment PostedAt
);

public sealed record Resolution(
    string Note,  // "" if the owner resolved without a note
    Moment ResolvedAt
);

public sealed record TrendingQuestion(
    long QuestionIndex,
    string Text,
    int RecentVoteCount  // Active votes with CastAt within the trailing 5 min
);

public sealed record RoomStats(
    int OpenQuestionCount,
    int ResolvedQuestionCount,
    long TotalVoteCount,  // Active votes across all non-deleted questions
    int AudienceCount     // Users present in the room within the presence TTL
);

public sealed record MoodSummary(
    ImmutableArray<int> Counts,  // Length 5; Counts[i] = present users at level i+1
    int VoterCount,              // Sum of Counts
    double? Average              // null when VoterCount == 0
);

// View models: each bundles everything a single reactive stream needs to feed one component,
// so a component subscribes to exactly one IAsyncEnumerable instead of composing several reads.

// One row of the active/recent town-hall list. The lobby stream carries only the ordered room ids;
// each row streams its own RoomCard, so a change in one room re-reads just that row.
public sealed record RoomCard(
    Room Room,
    int AudienceCount,     // Present users within the presence TTL
    int QuestionCount,     // Total questions (open + resolved)
    double? AverageMood    // Over presently-present users; null when none
);

public sealed record RoomView(
    Room Room,
    bool IsOwner,
    RoomStats Stats,
    // Whether the viewing session is signed in (guests are read-only)
    bool IsSignedIn,
    // The viewer's pseudonym for this room, shown next to the "Post anonymously" checkbox ("" for guests)
    string AnonName
);

// The lobby stream: the ordered active/recent room ids plus whether the viewer is signed in (which
// gates the create-room form). Each row streams its own RoomCard for live stats.
public sealed record LobbyView(
    ImmutableArray<string> RoomIds,
    bool IsSignedIn
);

public sealed record QuestionView(
    Question Question,
    string AuthorName,
    int VoteCount,
    bool HasOwnVote,
    // null for Open questions
    Resolution? Resolution
);

public sealed record MoodView(
    MoodSummary Summary,
    // This session's stored level (1..5) or null
    int? OwnLevel
);

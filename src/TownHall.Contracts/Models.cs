using MessagePack;

namespace TownHall;

public enum RoomStatus
{
    Paused = 0,   // Default at creation; not yet started or temporarily halted (timer frozen)
    Live = 1,
    Ended = 2,    // Running and now >= EndsAt; terminal
}

[MessagePackObject(true)]
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
    // Derived at read time; auto-invalidated at EndsAt while running
    RoomStatus Status,
    // Private rooms are reachable by link, but hidden from IRooms.ListActiveIds
    bool IsPrivate
);

public enum QuestionStatus { Open = 0, Resolved = 1 }

// Immutable after creation. Mutable facets live behind their own reads:
// vote count -> IQuestions.GetVoteCount, resolution -> IQuestions.GetResolution,
// author name -> IParticipants.GetName(AuthorId), so renames are reflected live.
[MessagePackObject(true)]
public sealed record Question(
    string RoomId,
    // Per-room, unique, monotonic; gaps possible after deletions
    long Index,
    // Public, stable id of the poster; resolve to a name via IParticipants.GetName
    string AuthorId,
    string Text,
    Moment PostedAt
);

[MessagePackObject(true)]
public sealed record Resolution(
    string Note,  // "" if the owner resolved without a note
    Moment ResolvedAt
);

[MessagePackObject(true)]
public sealed record ParticipantInfo(string Name);

[MessagePackObject(true)]
public sealed record TrendingQuestion(
    string RoomId,
    long QuestionIndex,
    int RecentVoteCount  // Active votes with CastAt within the trailing 5 min
);

[MessagePackObject(true)]
public sealed record RoomStats(
    int OpenQuestionCount,
    int ResolvedQuestionCount,
    long TotalVoteCount,  // Active votes across all non-deleted questions
    int AudienceCount     // Same number IPresence.GetAudienceCount returns
);

[MessagePackObject(true)]
public sealed record MoodSummary(
    ImmutableArray<int> Counts,  // Length 5; Counts[i] = present sessions at level i+1
    int VoterCount,              // Sum of Counts
    double? Average              // null when VoterCount == 0
);

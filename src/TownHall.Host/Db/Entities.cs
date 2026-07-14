using System.ComponentModel.DataAnnotations;

namespace TownHall.Host.Db;

// All DateTime values are UTC; values read back from Sqlite have Kind == Unspecified,
// so they're converted via .DefaultKind(DateTimeKind.Utc) wherever a Moment is needed.

public class DbRoom
{
    [Key]
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Link { get; set; } = "";
    public string Description { get; set; } = "";
    public string OwnerToken { get; set; } = "";
    public bool IsPrivate { get; set; }
    public long NextQuestionIndex { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime EndsAt { get; set; }
    // Non-null while paused (the timer is frozen); null while running
    public DateTime? PausedAt { get; set; }

    public RoomStatus GetStatus(Moment now)
        => PausedAt != null ? RoomStatus.Paused
            : now >= EndsAt.DefaultKind(DateTimeKind.Utc).ToMoment() ? RoomStatus.Ended
            : RoomStatus.Live;
}

public class DbRoomOwner // PK: (RoomId, SessionId)
{
    public string RoomId { get; set; } = "";
    public string SessionId { get; set; } = "";
}

public class DbParticipant // Key = public participant id (never the secret session id)
{
    [Key]
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

// The single-column Key enables batched lookups via IDbEntityResolver<string, DbQuestion>
public class DbQuestion // Key = "{RoomId}/{Index}"; ResolvedAt != null means Resolved
{
    [Key]
    public string Key { get; set; } = "";
    public string RoomId { get; set; } = "";
    public long Index { get; set; }
    public string AuthorId { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime PostedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string ResolutionNote { get; set; } = "";

    public static string ComposeKey(string roomId, long index)
        => $"{roomId}/{index}";
}

public class DbVote // PK: (RoomId, QuestionIndex, SessionId)
{
    public string RoomId { get; set; } = "";
    public long QuestionIndex { get; set; }
    public string SessionId { get; set; } = "";
    public DateTime CastAt { get; set; }
}

public class DbMood // PK: (RoomId, SessionId); Level is 1..5
{
    public string RoomId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public int Level { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace TownHall.Host.Db;

// All DateTime values are UTC; values read back from Sqlite have Kind == Unspecified,
// so they're converted via .DefaultKind(DateTimeKind.Utc) wherever a Moment is needed.

public class DbRoom
{
    [Key]
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string OwnerToken { get; set; } = "";
    public bool IsLive { get; set; }
    public bool IsPrivate { get; set; }
    public long NextQuestionIndex { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime ClosesAt { get; set; }

    public RoomStatus GetStatus(Moment now)
        => now >= ClosesAt.DefaultKind(DateTimeKind.Utc).ToMoment() ? RoomStatus.Ended
            : IsLive ? RoomStatus.Live : RoomStatus.Stopped;
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

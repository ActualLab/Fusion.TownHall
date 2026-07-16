using System.ComponentModel.DataAnnotations;

namespace TownHall.Db;

// All DateTime values are UTC (Npgsql maps them to "timestamp with time zone" and
// rejects writes with Kind != Utc); reads are normalized via .DefaultKind(DateTimeKind.Utc)
// wherever a Moment is needed.

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

public class DbRoomOwner // PK: (RoomId, UserId)
{
    public string RoomId { get; set; } = "";
    public string UserId { get; set; } = "";
}

// A signed-in user. Only authenticated users get a row here - guests are never stored.
public class DbUser // Key = public user id ("u-…")
{
    [Key]
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

// A WebAuthn/passkey credential bound to a user.
public class DbPasskeyCredential // Key = base64url credential id
{
    [Key]
    public string CredentialId { get; set; } = "";
    public string UserId { get; set; } = "";
    public byte[] PublicKey { get; set; } = [];
    public long SignCount { get; set; }
    public byte[] UserHandle { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}

// Links a Fusion session to a signed-in user; absence of a row means the session is a guest.
public class DbSessionUser // Key = secret session id
{
    [Key]
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
}

// The single-column Key enables batched lookups via IDbEntityResolver<string, DbQuestion>
public class DbQuestion // Key = "{RoomId}/{Index}"; ResolvedAt != null means Resolved
{
    [Key]
    public string Key { get; set; } = "";
    public string RoomId { get; set; } = "";
    public long Index { get; set; }
    public string AuthorId { get; set; } = "";  // A public user id ("u-…")
    public string Text { get; set; } = "";
    public DateTime PostedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string ResolutionNote { get; set; } = "";

    public static string ComposeKey(string roomId, long index)
        => $"{roomId}/{index}";
}

public class DbVote // PK: (RoomId, QuestionIndex, UserId)
{
    public string RoomId { get; set; } = "";
    public long QuestionIndex { get; set; }
    public string UserId { get; set; } = "";
    public DateTime CastAt { get; set; }
}

public class DbMood // PK: (RoomId, UserId); Level is 1..5
{
    public string RoomId { get; set; } = "";
    public string UserId { get; set; } = "";
    public int Level { get; set; }
}

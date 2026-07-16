namespace TownHall;

/// <summary>
/// A per-(user, room) anonymous author identity. The same user always posts under one stable
/// pseudonym within a room, but a different one in each room, and the id isn't linkable back to
/// the real user id. Stored as a question's AuthorId for anonymous posts; resolved to a name via
/// <see cref="NameGenerator"/>.
/// </summary>
public static class AnonId
{
    public const string Prefix = "anon-";

    public static string Of(string userId, string roomId)
        => Prefix + $"{userId}/{roomId}".GetXxHash3L().ToString("x16");

    public static bool Is(string id) => id.StartsWith(Prefix, StringComparison.Ordinal);
}

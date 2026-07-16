namespace TownHall;

public static class AuthExt
{
    // Frontend guard: turns a resolved (possibly guest) user id into a non-null one or throws.
    // Guests get null from IUsers.GetOwnUserId, so this is how "action requires sign-in" is enforced.
    public static string RequireSignedIn(this string? userId)
        => userId is { Length: > 0 }
            ? userId
            : throw new UnauthorizedAccessException("Please sign in to do this.");
}

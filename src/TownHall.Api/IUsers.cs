namespace TownHall;

// Frontend user API. Identity is implicit (the hub binds the connection's session).
// A session with no linked user is a guest - GetOwn returns null for it.
public interface IUsers
{
    // The signed-in user's own account, or null if the session is a guest.
    IAsyncEnumerable<UserFull?> GetOwn(CancellationToken cancellationToken = default);

    // Requires a signed-in user. Trimmed length 1..30. Renames the user everywhere it's shown.
    Task SetName(Users_SetName command, CancellationToken cancellationToken = default);
}

// ReSharper disable once InconsistentNaming
public sealed record Users_SetName(
    string Name
);

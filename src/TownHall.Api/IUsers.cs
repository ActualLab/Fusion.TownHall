using MessagePack;

namespace TownHall;

// Frontend user API: resolves a Session to a user and exposes public/own user data.
// A session with no linked user is a guest - GetOwnUserId / GetOwn return null for it.
public interface IUsers : IComputeService
{
    // The signed-in user's id for this session, or null if the session is a guest.
    [ComputeMethod]
    Task<string?> GetOwnUserId(Session session, CancellationToken cancellationToken = default);

    // The signed-in user's own account, or null if the session is a guest.
    [ComputeMethod]
    Task<UserFull?> GetOwn(Session session, CancellationToken cancellationToken = default);

    // Any user's public projection by id; null if unknown. Shared across sessions
    // (invalidated for everyone when that user renames).
    [ComputeMethod]
    Task<User?> Get(Session session, string userId, CancellationToken cancellationToken = default);

    // Requires a signed-in user. Trimmed length 1..30. Renames the user everywhere (GetOwn + Get).
    [CommandHandler]
    Task OnSetName(Users_SetName command, CancellationToken cancellationToken = default);
}

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Users_SetName(
    Session Session,
    string Name
) : ISessionCommand<Unit>, IDelegatingCommand;

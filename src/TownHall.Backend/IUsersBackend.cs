using MessagePack;

namespace TownHall;

// Backend user store: users, their passkey credentials, and session->user links. Id-based, no Session
// (a session id is just an opaque string key here). Credential reads are plain (ceremony-time only);
// user + link reads are compute methods so the frontend reacts to renames / sign-in / sign-out.
public interface IUsersBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<UserFull?> Get(string userId, CancellationToken cancellationToken = default);

    // The user linked to a session id, or null if none (guest).
    [ComputeMethod]
    Task<string?> GetUserIdBySession(string sessionId, CancellationToken cancellationToken = default);

    Task<string?> GetUserIdByCredential(string credentialId, CancellationToken cancellationToken = default);
    Task<StoredCredential?> GetCredential(string credentialId, CancellationToken cancellationToken = default);
    Task<StoredCredential[]> ListCredentials(string userId, CancellationToken cancellationToken = default);

    [CommandHandler]
    Task<string> Create(UsersBackend_Create command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task SetName(UsersBackend_SetName command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task LinkSession(UsersBackend_LinkSession command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task UnlinkSession(UsersBackend_UnlinkSession command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task AddCredential(UsersBackend_AddCredential command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task UpdateSignCount(UsersBackend_UpdateSignCount command, CancellationToken cancellationToken = default);
}

// A stored passkey credential (backend-only view; carries the public key material).
[MessagePackObject(true)]
public sealed record StoredCredential(
    string CredentialId,
    string UserId,
    byte[] PublicKey,
    long SignCount,
    byte[] UserHandle
);

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record UsersBackend_Create(
    string Name
) : ICommand<string>, IBackendCommand;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record UsersBackend_SetName(
    string UserId,
    string Name
) : ICommand<Unit>, IBackendCommand;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record UsersBackend_LinkSession(
    string SessionId,
    string UserId
) : ICommand<Unit>, IBackendCommand;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record UsersBackend_UnlinkSession(
    string SessionId
) : ICommand<Unit>, IBackendCommand;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record UsersBackend_AddCredential(
    string CredentialId,
    string UserId,
    byte[] PublicKey,
    long SignCount,
    byte[] UserHandle
) : ICommand<Unit>, IBackendCommand;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record UsersBackend_UpdateSignCount(
    string CredentialId,
    long SignCount
) : ICommand<Unit>, IBackendCommand;

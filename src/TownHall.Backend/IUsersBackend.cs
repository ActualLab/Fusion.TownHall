namespace TownHall;

// Backend user store: users, their passkey credentials, and session->user links. Id-based, no implicit
// identity (a session id is just an opaque string key here). Actions are plain command records.
public interface IUsersBackend
{
    Task<UserFull?> Get(string userId, CancellationToken cancellationToken = default);

    // The user linked to a session id, or null if none (guest).
    Task<string?> GetUserIdBySession(string sessionId, CancellationToken cancellationToken = default);

    // Display names for a batch of author ids (a mix of real user ids and anon ids).
    Task<Dictionary<string, string>> GetNames(IReadOnlyCollection<string> authorIds, CancellationToken cancellationToken = default);
    Task<StoredCredential?> GetCredential(string credentialId, CancellationToken cancellationToken = default);

    Task<string> Create(UsersBackend_Create command, CancellationToken cancellationToken = default);
    Task SetName(UsersBackend_SetName command, CancellationToken cancellationToken = default);
    Task LinkSession(UsersBackend_LinkSession command, CancellationToken cancellationToken = default);
    Task UnlinkSession(UsersBackend_UnlinkSession command, CancellationToken cancellationToken = default);
    Task AddCredential(UsersBackend_AddCredential command, CancellationToken cancellationToken = default);
    Task UpdateSignCount(UsersBackend_UpdateSignCount command, CancellationToken cancellationToken = default);
}

// A stored passkey credential (backend-only view; carries the public key material).
public sealed record StoredCredential(
    string CredentialId,
    string UserId,
    byte[] PublicKey,
    long SignCount,
    byte[] UserHandle
);

// ReSharper disable once InconsistentNaming
public sealed record UsersBackend_Create(
    string Name
);

// ReSharper disable once InconsistentNaming
public sealed record UsersBackend_SetName(
    string UserId,
    string Name
);

// ReSharper disable once InconsistentNaming
public sealed record UsersBackend_LinkSession(
    string SessionId,
    string UserId
);

// ReSharper disable once InconsistentNaming
public sealed record UsersBackend_UnlinkSession(
    string SessionId
);

// ReSharper disable once InconsistentNaming
public sealed record UsersBackend_AddCredential(
    string CredentialId,
    string UserId,
    byte[] PublicKey,
    long SignCount,
    byte[] UserHandle
);

// ReSharper disable once InconsistentNaming
public sealed record UsersBackend_UpdateSignCount(
    string CredentialId,
    long SignCount
);

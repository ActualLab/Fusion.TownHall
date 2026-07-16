using MessagePack;

namespace TownHall;

// Frontend passkey (WebAuthn) authentication API. The two option builders return the JSON the
// browser's navigator.credentials.* expects and stash a short-lived challenge server-side keyed by
// the session; the command handlers verify the browser's response, then create/link a user.
public interface IAuth : IComputeService
{
    // Builds navigator.credentials.create() options for a brand-new passkey + user with the given
    // display name (trimmed 1..30; a random name is used if blank). Not a compute method: each call
    // issues a fresh challenge.
    Task<string> GetRegistrationOptions(Session session, string name, CancellationToken cancellationToken = default);

    // Builds navigator.credentials.get() options for signing in with an existing (discoverable) passkey.
    Task<string> GetSignInOptions(Session session, CancellationToken cancellationToken = default);

    // Verifies the attestation, creates a new user with a default name, stores the credential and
    // links it to this session. Returns the new signed-in account.
    [CommandHandler]
    Task<UserFull> OnRegisterPasskey(Auth_RegisterPasskey command, CancellationToken cancellationToken = default);

    // Verifies the assertion against a stored credential and links the resolved user to this session.
    [CommandHandler]
    Task<UserFull> OnSignIn(Auth_SignIn command, CancellationToken cancellationToken = default);

    // Drops this session's user link, returning it to guest state. Idempotent.
    [CommandHandler]
    Task OnSignOut(Auth_SignOut command, CancellationToken cancellationToken = default);
}

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Auth_RegisterPasskey(
    Session Session,
    string AttestationJson
) : ISessionCommand<UserFull>;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Auth_SignIn(
    Session Session,
    string AssertionJson
) : ISessionCommand<UserFull>;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Auth_SignOut(
    Session Session
) : ISessionCommand<Unit>;

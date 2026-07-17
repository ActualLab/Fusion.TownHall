namespace TownHall;

// Frontend passkey (WebAuthn) authentication API. The two option builders return the JSON the
// browser's navigator.credentials.* expects and stash a short-lived challenge server-side keyed by
// the session; the command actions verify the browser's response, then create/link a user.
public interface IAuth
{
    // Builds navigator.credentials.create() options for a brand-new passkey + user with the given
    // display name (trimmed 1..30; a random name is used if blank). Each call issues a fresh challenge.
    Task<string> GetRegistrationOptions(string name, CancellationToken cancellationToken = default);

    // Builds navigator.credentials.get() options for signing in with an existing (discoverable) passkey.
    Task<string> GetSignInOptions(CancellationToken cancellationToken = default);

    // Verifies the attestation, creates a new user, stores the credential and
    // links it to this session. Returns the new signed-in account.
    Task<UserFull> RegisterPasskey(Auth_RegisterPasskey command, CancellationToken cancellationToken = default);

    // Verifies the assertion against a stored credential and links the resolved user to this session.
    Task<UserFull> SignIn(Auth_SignIn command, CancellationToken cancellationToken = default);

    // Drops this session's user link, returning it to guest state. Idempotent.
    Task SignOut(CancellationToken cancellationToken = default);
}

// ReSharper disable once InconsistentNaming
public sealed record Auth_RegisterPasskey(
    string AttestationJson
);

// ReSharper disable once InconsistentNaming
public sealed record Auth_SignIn(
    string AssertionJson
);

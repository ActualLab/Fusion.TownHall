using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.WebUtilities;

namespace TownHall.Host.Services;

// Frontend passkey (WebAuthn) service. Builds the browser ceremony options (stashing the challenge
// per-session), verifies the browser's response with Fido2, then creates/links a user via IUsersBackend.
public sealed class AuthService(ServerShared shared, Identity identity)
    : ServerService(shared, identity), IAuth
{
    // Fido2 is cheap and stateless given its config; build one for this per-connection instance.
    private IFido2 Fido2 => field ??= new Fido2(Shared.Fido2Config, null!);
    private IUsersBackend Users => Shared.Users;
    private PasskeyChallengeStore Challenges => Shared.Challenges;

    public Task<string> GetRegistrationOptions(string name, CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 30)
            name = NameGenerator.New(Guid.NewGuid().ToString("N"));
        var user = new Fido2User {
            Id = RandomBytes(16),           // the user handle; also stored on the credential
            Name = name,
            DisplayName = name,
        };
        var options = Fido2.RequestNewCredential(new RequestNewCredentialParams {
            User = user,
            ExcludeCredentials = [],
            AuthenticatorSelection = new AuthenticatorSelection {
                ResidentKey = ResidentKeyRequirement.Required,   // discoverable -> usernameless sign-in
                UserVerification = UserVerificationRequirement.Preferred,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });
        Challenges.StashRegistration(SessionId, options);
        return Task.FromResult(options.ToJson());
    }

    public Task<string> GetSignInOptions(CancellationToken cancellationToken = default)
    {
        var options = Fido2.GetAssertionOptions(new GetAssertionOptionsParams {
            AllowedCredentials = [],   // discoverable: the authenticator picks the credential
            UserVerification = UserVerificationRequirement.Preferred,
        });
        Challenges.StashSignIn(SessionId, options);
        return Task.FromResult(options.ToJson());
    }

    public async Task<UserFull> RegisterPasskey(Auth_RegisterPasskey command, CancellationToken cancellationToken = default)
    {
        var response = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(command.AttestationJson)
            ?? throw new InvalidOperationException("Invalid passkey response.");
        var options = Challenges.TakeRegistration(SessionId)
            ?? throw new InvalidOperationException("Passkey registration timed out. Please try again.");

        var credential = await Fido2.MakeNewCredentialAsync(new MakeNewCredentialParams {
            AttestationResponse = response,
            OriginalOptions = options,
            IsCredentialIdUniqueToUserCallback = static (_, _) => Task.FromResult(true),
        }, cancellationToken).ConfigureAwait(false);

        var credentialId = WebEncoders.Base64UrlEncode(credential.Id);
        var name = options.User.DisplayName;
        var userId = await Users.Create(new UsersBackend_Create(name), cancellationToken).ConfigureAwait(false);
        await Users.AddCredential(
            new UsersBackend_AddCredential(credentialId, userId, credential.PublicKey, credential.SignCount, options.User.Id),
            cancellationToken).ConfigureAwait(false);
        await Users.LinkSession(new UsersBackend_LinkSession(SessionId, userId), cancellationToken).ConfigureAwait(false);
        return (await Users.Get(userId, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<UserFull> SignIn(Auth_SignIn command, CancellationToken cancellationToken = default)
    {
        var response = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(command.AssertionJson)
            ?? throw new InvalidOperationException("Invalid passkey response.");
        var options = Challenges.TakeSignIn(SessionId)
            ?? throw new InvalidOperationException("Passkey sign-in timed out. Please try again.");

        var credentialId = WebEncoders.Base64UrlEncode(response.RawId);
        var stored = await Users.GetCredential(credentialId, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Unknown passkey. Create one first.");

        var result = await Fido2.MakeAssertionAsync(new MakeAssertionParams {
            AssertionResponse = response,
            OriginalOptions = options,
            StoredPublicKey = stored.PublicKey,
            StoredSignatureCounter = (uint)stored.SignCount,
            IsUserHandleOwnerOfCredentialIdCallback = (args, _) =>
                Task.FromResult(args.UserHandle == null || args.UserHandle.AsSpan().SequenceEqual(stored.UserHandle)),
        }, cancellationToken).ConfigureAwait(false);

        await Users.UpdateSignCount(new UsersBackend_UpdateSignCount(credentialId, result.SignCount), cancellationToken).ConfigureAwait(false);
        await Users.LinkSession(new UsersBackend_LinkSession(SessionId, stored.UserId), cancellationToken).ConfigureAwait(false);
        return (await Users.Get(stored.UserId, cancellationToken).ConfigureAwait(false))!;
    }

    public Task SignOut(CancellationToken cancellationToken = default)
        => Users.UnlinkSession(new UsersBackend_UnlinkSession(SessionId), cancellationToken);

    // Private methods

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }
}

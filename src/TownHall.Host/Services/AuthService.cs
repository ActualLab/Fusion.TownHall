using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.WebUtilities;

namespace TownHall.Host.Services;

// Frontend passkey (WebAuthn) service. Builds the browser ceremony options (stashing the challenge
// per-session), verifies the browser's response with Fido2, then creates/links a user via IUsersBackend.
public class AuthService(IServiceProvider services) : IAuth
{
    // IFido2 is scoped, but its inputs are singletons - build one long-lived instance for this singleton.
    private IFido2 Fido2 => field ??= new Fido2(
        services.GetRequiredService<Fido2Configuration>(),
        services.GetRequiredService<IMetadataService>());
    private PasskeyChallengeStore Challenges => field ??= services.GetRequiredService<PasskeyChallengeStore>();
    private IUsersBackend Backend => field ??= services.GetRequiredService<IUsersBackend>();
    private ICommander Commander => field ??= services.Commander();

    public Task<string> GetRegistrationOptions(Session session, string name, CancellationToken cancellationToken = default)
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
        Challenges.StashRegistration(session.Id, options);
        return Task.FromResult(options.ToJson());
    }

    public Task<string> GetSignInOptions(Session session, CancellationToken cancellationToken = default)
    {
        var options = Fido2.GetAssertionOptions(new GetAssertionOptionsParams {
            AllowedCredentials = [],   // discoverable: the authenticator picks the credential
            UserVerification = UserVerificationRequirement.Preferred,
        });
        Challenges.StashSignIn(session.Id, options);
        return Task.FromResult(options.ToJson());
    }

    public virtual async Task<UserFull> OnRegisterPasskey(Auth_RegisterPasskey command, CancellationToken cancellationToken = default)
    {
        var (session, attestationJson) = command;
        var response = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(attestationJson)
            ?? throw new InvalidOperationException("Invalid passkey response.");
        var options = Challenges.TakeRegistration(session.Id)
            ?? throw new InvalidOperationException("Passkey registration timed out. Please try again.");

        var credential = await Fido2.MakeNewCredentialAsync(new MakeNewCredentialParams {
            AttestationResponse = response,
            OriginalOptions = options,
            IsCredentialIdUniqueToUserCallback = static (_, _) => Task.FromResult(true),
        }, cancellationToken).ConfigureAwait(false);

        var credentialId = WebEncoders.Base64UrlEncode(credential.Id);
        var name = options.User.DisplayName;
        var userId = await Commander.Call(new UsersBackend_Create(name), true, cancellationToken).ConfigureAwait(false);
        await Commander
            .Call(new UsersBackend_AddCredential(
                credentialId, userId, credential.PublicKey, credential.SignCount, options.User.Id), true, cancellationToken)
            .ConfigureAwait(false);
        await Commander.Call(new UsersBackend_LinkSession(session.Id, userId), true, cancellationToken).ConfigureAwait(false);
        return (await Backend.Get(userId, cancellationToken).ConfigureAwait(false))!;
    }

    public virtual async Task<UserFull> OnSignIn(Auth_SignIn command, CancellationToken cancellationToken = default)
    {
        var (session, assertionJson) = command;
        var response = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(assertionJson)
            ?? throw new InvalidOperationException("Invalid passkey response.");
        var options = Challenges.TakeSignIn(session.Id)
            ?? throw new InvalidOperationException("Passkey sign-in timed out. Please try again.");

        var credentialId = WebEncoders.Base64UrlEncode(response.RawId);
        var stored = await Backend.GetCredential(credentialId, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Unknown passkey. Create one first.");

        var result = await Fido2.MakeAssertionAsync(new MakeAssertionParams {
            AssertionResponse = response,
            OriginalOptions = options,
            StoredPublicKey = stored.PublicKey,
            StoredSignatureCounter = (uint)stored.SignCount,
            IsUserHandleOwnerOfCredentialIdCallback = (args, _) =>
                Task.FromResult(args.UserHandle == null || args.UserHandle.AsSpan().SequenceEqual(stored.UserHandle)),
        }, cancellationToken).ConfigureAwait(false);

        await Commander
            .Call(new UsersBackend_UpdateSignCount(credentialId, result.SignCount), true, cancellationToken)
            .ConfigureAwait(false);
        await Commander
            .Call(new UsersBackend_LinkSession(session.Id, stored.UserId), true, cancellationToken)
            .ConfigureAwait(false);
        return (await Backend.Get(stored.UserId, cancellationToken).ConfigureAwait(false))!;
    }

    public virtual async Task OnSignOut(Auth_SignOut command, CancellationToken cancellationToken = default)
    {
        await Commander.Call(new UsersBackend_UnlinkSession(command.Session.Id), true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }
}

using Fido2NetLib;

namespace TownHall.Host.Services;

// Short-lived, in-memory store for the WebAuthn challenge/options issued between the "get options"
// call and the matching "register/sign-in" command, keyed by session id. Host-local and ephemeral.
public sealed class PasskeyChallengeStore(IServiceProvider services)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (object Options, Moment ExpiresAt)> _entries = new();

    private MomentClockSet Clocks { get; } = services.Clocks();

    public void StashRegistration(string sessionId, CredentialCreateOptions options)
        => Set($"reg:{sessionId}", options);

    public void StashSignIn(string sessionId, AssertionOptions options)
        => Set($"in:{sessionId}", options);

    public CredentialCreateOptions? TakeRegistration(string sessionId)
        => Take($"reg:{sessionId}") as CredentialCreateOptions;

    public AssertionOptions? TakeSignIn(string sessionId)
        => Take($"in:{sessionId}") as AssertionOptions;

    // Private methods

    private void Set(string key, object options)
        => _entries[key] = (options, Clocks.SystemClock.Now + Ttl);

    private object? Take(string key)
    {
        Prune();
        return _entries.TryRemove(key, out var entry) && entry.ExpiresAt >= Clocks.SystemClock.Now
            ? entry.Options
            : null;
    }

    private void Prune()
    {
        var now = Clocks.SystemClock.Now;
        foreach (var kv in _entries)
            if (kv.Value.ExpiresAt < now)
                _entries.TryRemove(kv);
    }
}

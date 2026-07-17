using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using TownHall.Db;

namespace TownHall.Host.Services;

// Base for the per-connection server-side services. Identity is captured once at construction, so the
// long-lived streams it returns keep serving the right caller without any ambient/AsyncLocal context.
public abstract class ServerService(ServerShared shared, Identity identity)
{
    protected ServerShared Shared { get; } = shared;
    protected ChangeTracker Changes { get; } = shared.Changes;
    protected PresenceStore Presence { get; } = shared.Presence;
    protected string SessionId { get; } = identity.SessionId;

    // The frontend guard: this session's signed-in user id, or null for a guest. Frontend services
    // resolve it, enforce sign-in/ownership, then delegate to the id-based backend services.
    protected Task<string?> GetUserId(CancellationToken cancellationToken)
        => Shared.Users.GetUserIdBySession(SessionId, cancellationToken);

    protected async Task<string> RequireUserId(CancellationToken cancellationToken)
        => await GetUserId(cancellationToken).ConfigureAwait(false) is { Length: > 0 } userId
            ? userId
            : throw new UnauthorizedAccessException("Please sign in to do this.");

    protected static TimeSpan? ToWake(Moment? nextChange)
        => nextChange is { } n ? n - Moment.Now + TimeSpan.FromMilliseconds(100) : null;

    protected Task<AppDbContext> CreateDbContext(CancellationToken cancellationToken)
        => Shared.DbContextFactory.CreateDbContextAsync(cancellationToken);

    // Persists the pending changes and notifies the given scopes. The notification fires even if
    // SaveChanges throws, because the write may still have committed (e.g. the connection dropped on
    // the commit ACK): a spurious notify only causes an idempotent re-read of the DB (the source of
    // truth), whereas a skipped notify can strand every open stream on that scope until the next
    // write, self-wake, or reconnect. Over-notify is safe; under-notify is not.
    protected async Task SaveAndNotify(AppDbContext db, CancellationToken cancellationToken, params string[] scopes)
    {
        try {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally {
            Changes.Notify(scopes);
        }
    }

    // Emits read() now, then re-emits whenever `scope` is notified or the optional per-read wake elapses
    // (the wake replaces Fusion's scheduled invalidation for time-based transitions).
    protected IAsyncEnumerable<T> Stream<T>(
        string scope,
        Func<CancellationToken, Task<(T Value, TimeSpan? Wake)>> read,
        CancellationToken cancellationToken)
        => Stream([scope], read, cancellationToken);

    // Session-aware streams pass ["room:{id}", "session:{sessionId}"] so they also re-read on sign-in.
    protected async IAsyncEnumerable<T> Stream<T>(
        string[] scopes,
        Func<CancellationToken, Task<(T Value, TimeSpan? Wake)>> read,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            // Capture the versions BEFORE reading so a change during the read isn't missed
            var versions = new long[scopes.Length];
            for (var i = 0; i < scopes.Length; i++)
                versions[i] = Changes.Version(scopes[i]);
            var (value, wake) = await ReadTraced(scopes[0], read, cancellationToken).ConfigureAwait(false);
            yield return value;

            await Changes.WaitForAnyChange(scopes, versions, wake, cancellationToken).ConfigureAwait(false);
        }
    }

    // The session scope this caller's session-aware streams watch (sign-in/out/rename re-read).
    protected string SessionScope => $"session:{SessionId}";

    // A span per stream read; the DB commands the read issues nest under it in the trace
    private static async Task<(T Value, TimeSpan? Wake)> ReadTraced<T>(
        string scope, Func<CancellationToken, Task<(T Value, TimeSpan? Wake)>> read, CancellationToken cancellationToken)
    {
        using var activity = TownHallTelemetry.Source.StartActivity("stream.read");
        activity?.SetTag("townhall.scope", scope);
        return await read(cancellationToken).ConfigureAwait(false);
    }
}

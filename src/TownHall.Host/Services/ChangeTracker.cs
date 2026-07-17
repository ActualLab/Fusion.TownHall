namespace TownHall.Host.Services;

/// <summary>
/// The reactivity core: writes call <see cref="Notify"/> for the scopes they touched, and every open
/// reactive read waits via <see cref="WaitForChange"/> on its single scope. Each scope keeps a version
/// counter and one shared completion, so there is at most one pending waiter object per active scope -
/// no per-subscription leak. This is the SignalR counterpart of Fusion's invalidation.
/// </summary>
public sealed class ChangeTracker
{
    // Scope conventions:
    //   "lobby"             - the active-rooms list or any field it shows changed
    //   "room:{roomId}"     - anything about a room changed (meta, status, questions, votes, mood, presence)
    //   "session:{sessionId}" - this session's own user changed (sign-in, sign-out, or its user renamed)
    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _scopes = new(StringComparer.Ordinal);

    public void Notify(params ReadOnlySpan<string> scopes)
    {
        List<TaskCompletionSource>? toComplete = null;
        lock (_lock) {
            foreach (var scope in scopes) {
                if (_scopes.TryGetValue(scope, out var entry)) {
                    entry.Version++;
                    (toComplete ??= []).Add(entry.Signal);
                    entry.Signal = NewSignal();
                }
                else
                    _scopes[scope] = new Entry { Version = 1, Signal = NewSignal() };
            }
        }
        if (toComplete == null)
            return;

        foreach (var tcs in toComplete)
            tcs.TrySetResult();
    }

    public long Version(string scope)
    {
        lock (_lock)
            return _scopes.TryGetValue(scope, out var entry) ? entry.Version : 0;
    }

    // Completes once ANY of `scopes` moves past its knownVersions[i], or `wake` elapses, or the token
    // fires. Session-aware streams use this to also react to a `session:{id}` change (sign-in/out/rename).
    public async Task WaitForAnyChange(
        string[] scopes, long[] knownVersions, TimeSpan? wake, CancellationToken cancellationToken)
    {
        if (scopes.Length == 1) {
            await WaitForChange(scopes[0], knownVersions[0], wake, cancellationToken).ConfigureAwait(false);
            return;
        }

        var hasWake = wake is { } w && w > TimeSpan.Zero;
        using var wakeCts = hasWake
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        var delay = wakeCts != null ? Task.Delay(wake!.Value, wakeCts.Token) : null;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var signals = new List<Task>(scopes.Length + 1);
                lock (_lock) {
                    for (var i = 0; i < scopes.Length; i++) {
                        if (!_scopes.TryGetValue(scopes[i], out var entry)) {
                            entry = new Entry { Version = 0, Signal = NewSignal() };
                            _scopes[scopes[i]] = entry;
                        }
                        if (entry.Version != knownVersions[i])
                            return;
                        signals.Add(entry.Signal.Task);
                    }
                }
                if (delay != null)
                    signals.Add(delay);
                var winner = await Task.WhenAny(signals).ConfigureAwait(false);
                if (winner == delay)
                    return; // Timer elapsed - re-read to reflect the time-based transition
            }
        }
        catch (OperationCanceledException) {
            // The stream is being torn down; let the caller observe cancellation on its next loop
        }
        finally {
            wakeCts?.Cancel();
        }
    }

    // Completes once `scope`'s version moves past knownVersion, or `wake` elapses, or the token fires.
    public async Task WaitForChange(string scope, long knownVersion, TimeSpan? wake, CancellationToken cancellationToken)
    {
        var hasWake = wake is { } w && w > TimeSpan.Zero;
        using var wakeCts = hasWake
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        var delay = wakeCts != null ? Task.Delay(wake!.Value, wakeCts.Token) : null;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                Task signal;
                lock (_lock) {
                    if (!_scopes.TryGetValue(scope, out var entry)) {
                        entry = new Entry { Version = 0, Signal = NewSignal() };
                        _scopes[scope] = entry;
                    }
                    if (entry.Version != knownVersion)
                        return;

                    signal = entry.Signal.Task;
                }
                if (delay == null) {
                    await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                else {
                    var winner = await Task.WhenAny(signal, delay).ConfigureAwait(false);
                    if (winner == delay)
                        return; // Timer elapsed - re-read to reflect the time-based transition
                }
            }
        }
        catch (OperationCanceledException) {
            // The stream is being torn down; let the caller observe cancellation on its next loop
        }
        finally {
            wakeCts?.Cancel(); // Stop the pending timer if a change (not the timer) woke us
        }
    }

    // Private methods

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Nested types

    private sealed class Entry
    {
        public long Version;
        public TaskCompletionSource Signal = null!;
    }
}

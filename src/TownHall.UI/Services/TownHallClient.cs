using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace TownHall.UI.Services;

/// <summary>
/// Owns the single SignalR connection to the server and exposes it as reconnect-resilient primitives:
/// <see cref="Stream{T}"/> re-establishes a server stream transparently across drops, and
/// <see cref="Invoke"/> waits briefly for the connection before issuing a command.
/// </summary>
public sealed class TownHallClient : IAsyncDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromMilliseconds(500);

    private readonly HubConnection _hub;
    private readonly SemaphoreSlim _startGate = new(1, 1);

    public HubConnectionState State => _hub.State;
    public bool IsConnected => _hub.State == HubConnectionState.Connected;
    // Fires on every connection-state transition, so the UI can show a "reconnecting" hint
    public event Action? StateChanged;

    public TownHallClient(NavigationManager nav)
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(BuildHubUrl(nav))
            .AddMessagePackProtocol(HubProtocolConfig.Configure)
            .WithAutomaticReconnect(new InfiniteRetryPolicy())
            .Build();
        _hub.Reconnecting += _ => { StateChanged?.Invoke(); return Task.CompletedTask; };
        _hub.Reconnected += _ => { StateChanged?.Invoke(); return Task.CompletedTask; };
        _hub.Closed += _ => { StateChanged?.Invoke(); return Task.CompletedTask; };
    }

    public async Task EnsureStarted(CancellationToken cancellationToken = default)
    {
        if (_hub.State != HubConnectionState.Disconnected)
            return;

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            if (_hub.State == HubConnectionState.Disconnected) {
                await _hub.StartAsync(cancellationToken).ConfigureAwait(false);
                StateChanged?.Invoke();
            }
        }
        finally {
            _startGate.Release();
        }
    }

    public async Task<T> Invoke<T>(string method, CancellationToken cancellationToken, params object?[] args)
    {
        await WaitForConnected(cancellationToken).ConfigureAwait(false);
        return await _hub.InvokeCoreAsync<T>(method, args, cancellationToken).ConfigureAwait(false);
    }

    public async Task Invoke(string method, CancellationToken cancellationToken, params object?[] args)
    {
        await WaitForConnected(cancellationToken).ConfigureAwait(false);
        await _hub.InvokeCoreAsync(method, args, cancellationToken).ConfigureAwait(false);
    }

    // A server stream re-subscribed across reconnects: on a drop the inner enumeration ends, we wait for
    // the connection to come back, then re-open the stream (which re-emits its current value).
    public async IAsyncEnumerable<T> Stream<T>(
        string method, [EnumeratorCancellation] CancellationToken cancellationToken, params object?[] args)
    {
        while (!cancellationToken.IsCancellationRequested) {
            IAsyncEnumerator<T>? enumerator = null;
            try {
                await EnsureStarted(cancellationToken).ConfigureAwait(false);
                if (_hub.State == HubConnectionState.Connected)
                    enumerator = _hub.StreamAsyncCore<T>(method, args, cancellationToken).GetAsyncEnumerator(cancellationToken);
            }
            catch (OperationCanceledException) {
                yield break;
            }
            catch {
                // Not connected yet (e.g. mid-reconnect); fall through to the delay + retry below
            }

            if (enumerator != null) {
                while (true) {
                    var hasValue = false;
                    var value = default(T)!;
                    try {
                        hasValue = await enumerator.MoveNextAsync().ConfigureAwait(false);
                        if (hasValue)
                            value = enumerator.Current;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                        hasValue = false;
                    }
                    catch {
                        hasValue = false; // Dropped; break out to reconnect
                    }
                    if (!hasValue)
                        break;

                    yield return value;
                }
                try { await enumerator.DisposeAsync().ConfigureAwait(false); } catch { /* ignore */ }
            }

            if (cancellationToken.IsCancellationRequested)
                yield break;

            try { await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { yield break; }
        }
    }

    public async ValueTask DisposeAsync()
        => await _hub.DisposeAsync().ConfigureAwait(false);

    // Private methods

    private async Task WaitForConnected(CancellationToken cancellationToken)
    {
        await EnsureStarted(cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < 50 && _hub.State != HubConnectionState.Connected; i++) {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildHubUrl(NavigationManager nav)
    {
        var url = new Uri(new Uri(nav.BaseUri), "townhall-hub").ToString();
        // Carry a ?session=<id> testing override (dev only) onto the hub connection so the server can
        // bind this connection to that session instead of the browser cookie.
        var query = new Uri(nav.Uri).Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries)) {
            var kv = pair.Split('=', 2);
            if (kv is ["session", var value] && value.Length >= 8)
                return url + "?session=" + Uri.EscapeDataString(Uri.UnescapeDataString(value));
        }
        return url;
    }

    // Nested types

    private sealed class InfiniteRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
            => TimeSpan.FromSeconds(Math.Min(1 + retryContext.PreviousRetryCount, 5));
    }
}

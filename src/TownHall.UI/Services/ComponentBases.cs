using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TownHall.UI.Services;

namespace TownHall.UI;

// Shared conveniences for the app's interactive components.
public abstract class AppComponentBase : ComponentBase
{
    [Inject] protected NavigationManager Nav { get; set; } = null!;
    [Inject] protected IJSRuntime JS { get; set; } = null!;
    [Inject] protected UiCommander UICommander { get; set; } = null!;
}

/// <summary>
/// Subscribes to one reactive stream and re-renders on each value it yields - the SignalR counterpart
/// of Fusion's <c>ComputedStateComponent&lt;T&gt;</c>. The client's stream wrapper hides reconnects,
/// so the subscription simply keeps yielding across drops.
/// </summary>
public abstract class StateComponent<T> : AppComponentBase, IAsyncDisposable
{
    private readonly CancellationTokenSource _disposeCts = new();
    private CancellationTokenSource? _runCts;
    private object? _stateKey;
    private bool _started;

    protected T? State { get; private set; }
    protected bool HasValue { get; private set; }

    protected abstract IAsyncEnumerable<T> Stream(CancellationToken cancellationToken);

    // Override to return the parameter(s) the stream depends on; the stream restarts when it changes.
    protected virtual object? StateKey => null;

    protected override void OnParametersSet()
    {
        if (_started && Equals(StateKey, _stateKey))
            return;

        StartStream();
    }

    // Re-subscribe the stream now (e.g. after a local field the read depends on changed via an event,
    // which doesn't run OnParametersSet).
    protected void Restart()
    {
        StartStream();
        StateHasChanged();
    }

    private void StartStream()
    {
        _started = true;
        _stateKey = StateKey;
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
        HasValue = false;
        State = default;
        _ = RunAsync(_runCts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _disposeCts.CancelAsync().ConfigureAwait(false);
        _runCts?.Dispose();
        _disposeCts.Dispose();
    }

    // Private methods

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            try {
                await foreach (var value in Stream(cancellationToken).WithCancellation(cancellationToken)) {
                    State = value;
                    HasValue = true;
                    await InvokeAsync(StateHasChanged).ConfigureAwait(true);
                }
            }
            catch (OperationCanceledException) {
                return;
            }
            catch {
                // Unexpected stream failure; brief pause, then retry below
            }
            if (cancellationToken.IsCancellationRequested)
                return;

            try { await Task.Delay(500, cancellationToken).ConfigureAwait(true); }
            catch (OperationCanceledException) { return; }
        }
    }
}

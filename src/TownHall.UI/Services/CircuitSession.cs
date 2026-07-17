namespace TownHall.UI.Services;

// Server-render only: carries the browser's session id into the per-circuit direct services (in WASM
// the session comes from the hub connection instead, so this is never registered there).
public sealed class CircuitSession
{
    private string? _sessionId;

    public string SessionId => _sessionId
        ?? throw new InvalidOperationException("The circuit session has not been initialized yet.");

    public void Set(string sessionId)
        => _sessionId = sessionId;
}

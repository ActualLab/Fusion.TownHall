namespace TownHall.UI.Services;

// Holds the render mode the current session is running in (set by App from the host-page cookie), so
// the app-bar switch can show it. Registered on both the server and the WASM container.
public sealed class RenderModeState
{
    public RenderModeDef Current { get; set; } = RenderModeDef.Default;
}

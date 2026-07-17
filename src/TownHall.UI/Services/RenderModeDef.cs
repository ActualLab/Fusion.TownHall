using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace TownHall.UI.Services;

// A selectable Blazor render mode for the app-bar switch (Auto / Server / WASM), mirroring the switch
// in the Fusion sample. Prerender is off so the interactive runtime owns rendering in every mode.
public sealed record RenderModeDef(string Key, string Title, IComponentRenderMode Mode)
{
    public static readonly RenderModeDef Auto = new("a", "Auto", new InteractiveAutoRenderMode(prerender: false));
    public static readonly RenderModeDef Server = new("s", "Server", new InteractiveServerRenderMode(prerender: false));
    public static readonly RenderModeDef Wasm = new("w", "WASM", new InteractiveWebAssemblyRenderMode(prerender: false));

    public static readonly RenderModeDef[] All = [Auto, Server, Wasm];
    public static readonly RenderModeDef Default = Auto;

    public static RenderModeDef GetOrDefault(string? key)
        => All.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.Ordinal)) ?? Default;
}

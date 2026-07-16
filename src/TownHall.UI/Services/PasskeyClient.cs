using Microsoft.JSInterop;

namespace TownHall.UI.Services;

// Thin wrapper over wwwroot/js/passkey.js: runs the browser WebAuthn ceremony and returns its JSON.
public sealed class PasskeyClient(IJSRuntime js)
{
    private IJSObjectReference? _module;

    public async Task<string> Register(string optionsJson)
        => await (await Module().ConfigureAwait(false)).InvokeAsync<string>("register", optionsJson).ConfigureAwait(false);

    public async Task<string> Authenticate(string optionsJson)
        => await (await Module().ConfigureAwait(false)).InvokeAsync<string>("authenticate", optionsJson).ConfigureAwait(false);

    private async ValueTask<IJSObjectReference> Module()
        => _module ??= await js.InvokeAsync<IJSObjectReference>("import", "/js/passkey.js").ConfigureAwait(false);
}

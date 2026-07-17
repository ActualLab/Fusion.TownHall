using Microsoft.AspNetCore.SignalR;

namespace TownHall.Host;

// Domain validation/permission failures are user-facing, so translate them to HubException - whose
// message SignalR always delivers to the client verbatim (unlike arbitrary server exceptions).
// (Tracing of hub invocations is handled natively by SignalR's built-in ActivitySource.)
public sealed class ErrorHubFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try {
            return await next(invocationContext);
        }
        catch (Exception e) when (e is
            ArgumentException or InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException) {
            throw new HubException(e.Message);
        }
    }
}

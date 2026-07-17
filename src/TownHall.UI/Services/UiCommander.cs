using MudBlazor;

namespace TownHall.UI.Services;

// Runs a hub command, surfacing any failure (validation, rejected owner action, dropped connection)
// as an error snackbar. Mirrors the role Fusion's UICommander + UIActionFailureTracker played.
public sealed class UiCommander(ISnackbar snackbar)
{
    public async Task<bool> Run(Func<Task> command)
    {
        try {
            await command().ConfigureAwait(true);
            return true;
        }
        catch (Exception e) {
            snackbar.Add(Describe(e), Severity.Error);
            return false;
        }
    }

    public async Task<Result<T>> Run<T>(Func<Task<T>> command)
    {
        try {
            return new Result<T>(await command().ConfigureAwait(true), null);
        }
        catch (Exception e) {
            snackbar.Add(Describe(e), Severity.Error);
            return new Result<T>(default!, e);
        }
    }

    // Private methods

    private static string Describe(Exception e)
        => e.Message is { Length: > 0 } message ? message : "Action failed.";
}

namespace TownHall.UI.Services;

/// <summary>
/// Lets a page override the app-bar title (e.g. the room title on /room pages).
/// Null means the default "Town Hall" brand is shown.
/// </summary>
public sealed class LayoutState
{
    public string? Title { get; private set; }
    public event Action? Changed;

    public void SetTitle(string? title)
    {
        if (Title == title)
            return;

        Title = title;
        Changed?.Invoke();
    }
}

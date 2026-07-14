namespace TownHall.UI.Services;

/// <summary>
/// Human-friendly relative time formatting for the UI.
/// </summary>
public static class TimeText
{
    public static string Ago(Moment moment)
    {
        var delta = Moment.Now - moment;
        return delta switch {
            { TotalSeconds: < 60 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)delta.TotalMinutes}m ago",
            { TotalHours: < 24 } => $"{(int)delta.TotalHours}h ago",
            _ => $"{(int)delta.TotalDays}d ago",
        };
    }

    public static string Left(Moment endsAt)
        => Left(endsAt - Moment.Now);

    public static string Left(TimeSpan delta)
    {
        return delta switch {
            { Ticks: <= 0 } => "ended",
            { TotalMinutes: < 1 } => "less than a minute left",
            { TotalHours: < 1 } => $"{(int)delta.TotalMinutes} min left",
            _ => $"{(int)delta.TotalHours} h {delta.Minutes} min left",
        };
    }

    public static string Countdown(TimeSpan delta)
    {
        var sign = "";
        if (delta < TimeSpan.Zero) {
            sign = "−";
            delta = -delta;
        }
        return $"{sign}{(int)delta.TotalHours}:{delta.Minutes:D2}:{delta.Seconds:D2}";
    }
}

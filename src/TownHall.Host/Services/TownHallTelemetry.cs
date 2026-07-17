using System.Diagnostics;

namespace TownHall.Host.Services;

// The app's own tracing source. Spans on it (a hub invocation, a stream read) become the parent of the
// Npgsql command spans they trigger, so a trace shows how each SignalR call maps to DB hits.
public static class TownHallTelemetry
{
    public const string SourceName = "TownHall";
    public static readonly ActivitySource Source = new(SourceName);
}

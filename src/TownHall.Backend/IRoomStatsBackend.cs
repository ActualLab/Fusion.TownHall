namespace TownHall;

// Backend room-stats aggregation. Read-only; composes on IQuestionsBackend + IPresenceBackend.
// ReadTrending returns the value plus the moment the ranking next changes on its own (a vote aging
// out of the trailing window).
public interface IRoomStatsBackend
{
    Task<(ImmutableArray<TrendingQuestion> Value, Moment? NextChange)> ReadTrending(
        string roomId, int limit, CancellationToken cancellationToken = default);
}

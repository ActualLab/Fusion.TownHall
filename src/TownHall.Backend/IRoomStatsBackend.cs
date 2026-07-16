namespace TownHall;

// Backend room-stats aggregation. Read-only; composes on IQuestionsBackend + IPresenceBackend.
public interface IRoomStatsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ImmutableArray<TrendingQuestion>> ListTrending(string roomId, int limit, CancellationToken cancellationToken = default);

    [ComputeMethod]
    Task<RoomStats> GetStats(string roomId, CancellationToken cancellationToken = default);
}

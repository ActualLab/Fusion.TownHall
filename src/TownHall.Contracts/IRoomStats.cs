namespace TownHall;

public interface IRoomStats : IComputeService
{
    // Open questions ranked by RecentVoteCount desc (ties: higher total votes first); entries with
    // RecentVoteCount == 0 excluded. "Recent" = active votes cast within the trailing 5 minutes.
    [ComputeMethod]
    Task<ImmutableArray<TrendingQuestion>> GetTrending(Session session, string roomId, int limit, CancellationToken cancellationToken = default);

    [ComputeMethod]
    Task<RoomStats> GetStats(Session session, string roomId, CancellationToken cancellationToken = default);
}

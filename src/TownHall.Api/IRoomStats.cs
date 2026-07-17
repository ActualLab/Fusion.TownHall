namespace TownHall;

public interface IRoomStats
{
    // Open questions ranked by RecentVoteCount desc (ties: higher total votes first); entries with
    // RecentVoteCount == 0 excluded. "Recent" = active votes cast within the trailing 5 minutes.
    IAsyncEnumerable<ImmutableArray<TrendingQuestion>> ListTrending(string roomId, int limit, CancellationToken cancellationToken = default);
}

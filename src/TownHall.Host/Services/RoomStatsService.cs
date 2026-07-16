namespace TownHall.Host.Services;

// Frontend room-stats service: read-only, open to guests; delegates straight to IRoomStatsBackend.
public class RoomStatsService(IServiceProvider services) : IRoomStats
{
    private IRoomStatsBackend Backend => field ??= services.GetRequiredService<IRoomStatsBackend>();

    public virtual Task<ImmutableArray<TrendingQuestion>> ListTrending(Session session, string roomId, int limit, CancellationToken cancellationToken = default)
        => Backend.ListTrending(roomId, limit, cancellationToken);

    public virtual Task<RoomStats> GetStats(Session session, string roomId, CancellationToken cancellationToken = default)
        => Backend.GetStats(roomId, cancellationToken);
}

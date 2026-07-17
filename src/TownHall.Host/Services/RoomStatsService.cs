namespace TownHall.Host.Services;

// Frontend room-stats service: read-only, open to guests; delegates straight to IRoomStatsBackend.
public sealed class RoomStatsService(ServerShared shared, Identity identity)
    : ServerService(shared, identity), IRoomStats
{
    public IAsyncEnumerable<ImmutableArray<TrendingQuestion>> ListTrending(
        string roomId, int limit, CancellationToken cancellationToken = default)
        => Stream($"room:{roomId}", async ct => {
            var (value, nextChange) = await Shared.RoomStats.ReadTrending(roomId, limit, ct).ConfigureAwait(false);
            return (value, ToWake(nextChange));
        }, cancellationToken);
}

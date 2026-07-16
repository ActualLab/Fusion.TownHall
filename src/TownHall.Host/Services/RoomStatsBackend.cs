using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;
using TownHall.Db;

namespace TownHall.Host.Services;

public class RoomStatsBackend(IServiceProvider services) : DbServiceBase<AppDbContext>(services), IRoomStatsBackend
{
    public static readonly TimeSpan TrendingWindow = TimeSpan.FromMinutes(5);

    private IQuestionsBackend Questions => field ??= Services.GetRequiredService<IQuestionsBackend>();
    private IPresenceBackend Presence => field ??= Services.GetRequiredService<IPresenceBackend>();

    [ComputeMethod]
    public virtual async Task<ImmutableArray<TrendingQuestion>> ListTrending(string roomId, int limit, CancellationToken cancellationToken = default)
    {
        await Questions.PseudoVotes(roomId, cancellationToken).ConfigureAwait(false);
        var openIds = await Questions.ListOpen(roomId, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var now = Clocks.SystemClock.Now;
        var windowStart = (now - TrendingWindow).ToDateTime();
        var recentVotes = await dbContext.Votes
            .Where(v => v.RoomId == roomId && v.CastAt > windowStart)
            .Select(v => new { v.QuestionIndex, v.CastAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        // The ranking changes on its own as votes age out of the window
        if (recentVotes.Count != 0) {
            var oldestCastAt = recentVotes.Min(v => v.CastAt).DefaultKind(DateTimeKind.Utc).ToMoment();
            Computed.GetCurrent().Invalidate(oldestCastAt + TrendingWindow - now + TimeSpan.FromMilliseconds(100));
        }

        var openIdSet = openIds.ToHashSet();
        var candidates = recentVotes
            .Where(v => openIdSet.Contains(v.QuestionIndex))
            .GroupBy(v => v.QuestionIndex)
            .Select(g => (Index: g.Key, RecentCount: g.Count()))
            .ToList();
        var totalCounts = new Dictionary<long, int>();
        foreach (var (index, _) in candidates)
            totalCounts[index] = await Questions.GetVoteCount(roomId, index, cancellationToken).ConfigureAwait(false);
        return [
            ..candidates
                .OrderByDescending(c => c.RecentCount)
                .ThenByDescending(c => totalCounts[c.Index])
                .ThenBy(c => c.Index)
                .Take(limit)
                .Select(c => new TrendingQuestion(roomId, c.Index, c.RecentCount))
        ];
    }

    [ComputeMethod]
    public virtual async Task<RoomStats> GetStats(string roomId, CancellationToken cancellationToken = default)
    {
        var openCount = (await Questions.ListOpen(roomId, cancellationToken).ConfigureAwait(false)).Length;
        var resolvedCount = (await Questions.ListResolved(roomId, cancellationToken).ConfigureAwait(false)).Length;
        var totalVoteCount = await GetTotalVoteCount(roomId, cancellationToken).ConfigureAwait(false);
        var audienceCount = (await Presence.GetPresentUsers(roomId).ConfigureAwait(false)).Count;
        return new RoomStats(openCount, resolvedCount, totalVoteCount, audienceCount);
    }

    // Protected/internal methods

    [ComputeMethod]
    protected virtual async Task<long> GetTotalVoteCount(string roomId, CancellationToken cancellationToken = default)
    {
        await Questions.PseudoVotes(roomId, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        return await dbContext.Votes
            .LongCountAsync(v => v.RoomId == roomId, cancellationToken)
            .ConfigureAwait(false);
    }
}

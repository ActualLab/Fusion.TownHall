using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;
using TownHall.Db;

namespace TownHall.Host.Services;

public class RoomStatsService(IServiceProvider services) : DbServiceBase<AppDbContext>(services), IRoomStats
{
    public static readonly TimeSpan TrendingWindow = TimeSpan.FromMinutes(5);

    private QuestionsService Questions => field ??= (QuestionsService)Services.GetRequiredService<IQuestions>();
    private PresenceService Presence => field ??= (PresenceService)Services.GetRequiredService<IPresence>();

    public virtual async Task<ImmutableArray<TrendingQuestion>> ListTrending(Session session, string roomId, int limit, CancellationToken cancellationToken = default)
        => await GetRoomTrending(roomId, limit, cancellationToken).ConfigureAwait(false);

    public virtual async Task<RoomStats> GetStats(Session session, string roomId, CancellationToken cancellationToken = default)
    {
        var openCount = (await Questions.ListOpenQuestionIds(roomId, cancellationToken).ConfigureAwait(false)).Length;
        var resolvedCount = (await Questions.ListResolvedQuestionIds(roomId, cancellationToken).ConfigureAwait(false)).Length;
        var totalVoteCount = await GetTotalVoteCount(roomId, cancellationToken).ConfigureAwait(false);
        var audienceCount = (await Presence.GetPresentSessions(roomId).ConfigureAwait(false)).Count;
        return new RoomStats(openCount, resolvedCount, totalVoteCount, audienceCount);
    }

    // Protected/internal methods

    [ComputeMethod]
    protected virtual async Task<ImmutableArray<TrendingQuestion>> GetRoomTrending(string roomId, int limit, CancellationToken cancellationToken = default)
    {
        await Questions.PseudoVotes(roomId).ConfigureAwait(false);
        var openIds = await Questions.ListOpenQuestionIds(roomId, cancellationToken).ConfigureAwait(false);

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
            totalCounts[index] = await Questions.GetQuestionVoteCount(roomId, index, cancellationToken).ConfigureAwait(false);
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
    protected virtual async Task<long> GetTotalVoteCount(string roomId, CancellationToken cancellationToken = default)
    {
        await Questions.PseudoVotes(roomId).ConfigureAwait(false);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        return await dbContext.Votes
            .LongCountAsync(v => v.RoomId == roomId, cancellationToken)
            .ConfigureAwait(false);
    }
}

using Microsoft.EntityFrameworkCore;
using TownHall.Db;

namespace TownHall.Host.Services;

public sealed class RoomStatsBackend(IDbContextFactory<AppDbContext> dbContextFactory, ChangeTracker changes)
    : BackendService(dbContextFactory, changes), IRoomStatsBackend
{
    public static readonly TimeSpan TrendingWindow = TimeSpan.FromMinutes(5);

    public async Task<(ImmutableArray<TrendingQuestion> Value, Moment? NextChange)> ReadTrending(
        string roomId, int limit, CancellationToken cancellationToken = default)
    {
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var now = Moment.Now;
        var windowStart = (now - TrendingWindow).ToDateTime();
        var openQuestions = await dbContext.Questions
            .Where(q => q.RoomId == roomId && q.ResolvedAt == null)
            .Select(q => new { q.Index, q.Text })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var textByIndex = openQuestions.ToDictionary(q => q.Index, q => q.Text);
        var recentVotes = await dbContext.Votes
            .Where(v => v.RoomId == roomId && v.CastAt > windowStart)
            .Select(v => new { v.QuestionIndex, v.CastAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The ranking changes on its own as votes age out of the window
        Moment? nextChange = recentVotes.Count != 0
            ? recentVotes.Min(v => v.CastAt).DefaultKind(DateTimeKind.Utc).ToMoment() + TrendingWindow
            : null;

        var candidates = recentVotes
            .Where(v => textByIndex.ContainsKey(v.QuestionIndex))
            .GroupBy(v => v.QuestionIndex)
            .Select(g => (Index: g.Key, RecentCount: g.Count()))
            .ToList();
        var totalCounts = new Dictionary<long, int>();
        foreach (var (index, _) in candidates)
            totalCounts[index] = await dbContext.Votes
                .CountAsync(v => v.RoomId == roomId && v.QuestionIndex == index, cancellationToken).ConfigureAwait(false);
        var result = candidates
            .OrderByDescending(c => c.RecentCount)
            .ThenByDescending(c => totalCounts[c.Index])
            .ThenBy(c => c.Index)
            .Take(limit)
            .Select(c => new TrendingQuestion(c.Index, textByIndex[c.Index], c.RecentCount))
            .ToImmutableArray();
        return (result, nextChange);
    }
}

using Microsoft.EntityFrameworkCore;
using TownHall.Db;

namespace TownHall.Host.Services;

public sealed class MoodBackend(
    IDbContextFactory<AppDbContext> dbContextFactory, ChangeTracker changes, PresenceStore presence)
    : BackendService(dbContextFactory, changes), IMoodBackend
{
    public async Task<(MoodSummary Summary, Moment? NextChange)> ReadSummary(string roomId, CancellationToken cancellationToken = default)
    {
        var (presentIds, nextExpiry) = presence.Present(roomId);
        var counts = new int[5];
        if (presentIds.Length != 0) {
            var ids = presentIds.ToArray();
            var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
            await using var _1 = dbContext.ConfigureAwait(false);

            var levels = await dbContext.Moods
                .Where(m => m.RoomId == roomId && ids.Contains(m.UserId))
                .Select(m => m.Level)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var level in levels)
                counts[level - 1]++;
        }
        var voterCount = counts.Sum();
        double? average = voterCount == 0
            ? null
            : counts.Select((count, i) => count * (i + 1)).Sum() / (double)voterCount;
        return (new MoodSummary([..counts], voterCount, average), nextExpiry);
    }

    public async Task SetMood(MoodBackend_Set command, CancellationToken cancellationToken = default)
    {
        var (roomId, userId, level) = command;
        if (level is < 1 or > 5)
            throw new ArgumentException("Mood level must be in 1..5.");

        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        var now = Moment.Now;
        if (dbRoom.GetStatus(now) != RoomStatus.Live)
            throw new InvalidOperationException("This town hall is not live.");

        var dbMood = await dbContext.Moods
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (dbMood == null)
            dbContext.Add(new DbMood { RoomId = roomId, UserId = userId, Level = level });
        else
            dbMood.Level = level;
        await SaveAndNotify(dbContext, cancellationToken, $"room:{roomId}");
    }
}

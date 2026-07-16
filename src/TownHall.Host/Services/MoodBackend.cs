using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;
using TownHall.Db;

namespace TownHall.Host.Services;

public class MoodBackend(IServiceProvider services) : DbServiceBase<AppDbContext>(services), IMoodBackend
{
    private IPresenceBackend Presence => field ??= Services.GetRequiredService<IPresenceBackend>();

    [ComputeMethod]
    public virtual async Task<MoodSummary> GetSummary(string roomId, CancellationToken cancellationToken = default)
    {
        var present = await Presence.GetPresentUsers(roomId).ConfigureAwait(false);
        var counts = new int[5];
        if (present.Count != 0) {
            var presentIds = present.UserIds;
            var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
            await using var _1 = dbContext.ConfigureAwait(false);

            var levels = await dbContext.Moods
                .Where(m => m.RoomId == roomId && presentIds.Contains(m.UserId))
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
        return new MoodSummary([..counts], voterCount, average);
    }

    [ComputeMethod]
    public virtual async Task<int?> GetOwn(string roomId, string userId, CancellationToken cancellationToken = default)
    {
        if (userId.Length == 0)
            return null;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbMood = await dbContext.Moods
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        return dbMood?.Level;
    }

    public virtual async Task OnSetMood(MoodBackend_Set command, CancellationToken cancellationToken = default)
    {
        var (roomId, userId, level) = command;
        if (Invalidation.IsActive) {
            _ = GetSummary(roomId, default);
            _ = GetOwn(roomId, userId, default);
            return;
        }

        if (level is < 1 or > 5)
            throw new ArgumentException("Mood level must be in 1..5.");

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        if (dbRoom.GetStatus(now) != RoomStatus.Live)
            throw new InvalidOperationException("This town hall is not live.");

        var dbMood = await dbContext.Moods
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (dbMood == null)
            dbContext.Add(new DbMood { RoomId = roomId, UserId = userId, Level = level });
        else
            dbMood.Level = level;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

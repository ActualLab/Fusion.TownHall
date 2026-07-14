using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;
using TownHall.Host.Db;

namespace TownHall.Host.Services;

public class MoodService(IServiceProvider services) : DbServiceBase<AppDbContext>(services), IMood
{
    private PresenceService Presence => field ??= (PresenceService)Services.GetRequiredService<IPresence>();

    public virtual async Task<MoodSummary> GetSummary(Session session, string roomId, CancellationToken cancellationToken = default)
        => await GetRoomMoodSummary(roomId, cancellationToken).ConfigureAwait(false);

    public virtual async Task<int?> GetOwn(Session session, string roomId, CancellationToken cancellationToken = default)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        string sessionId = session.Id;
        var dbMood = await dbContext.Moods
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.SessionId == sessionId, cancellationToken)
            .ConfigureAwait(false);
        return dbMood?.Level;
    }

    public virtual async Task OnSetMood(Mood_Set command, CancellationToken cancellationToken = default)
    {
        var (session, roomId, level) = command;
        if (Invalidation.IsActive) {
            _ = GetRoomMoodSummary(roomId, default);
            _ = GetOwn(session, roomId, default);
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

        string sessionId = session.Id;
        var dbMood = await dbContext.Moods
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.SessionId == sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (dbMood == null)
            dbContext.Add(new DbMood { RoomId = roomId, SessionId = sessionId, Level = level });
        else
            dbMood.Level = level;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // Protected/internal methods

    [ComputeMethod]
    protected virtual async Task<MoodSummary> GetRoomMoodSummary(string roomId, CancellationToken cancellationToken = default)
    {
        var presentIds = await Presence.GetPresentSessionIds(roomId).ConfigureAwait(false);
        var counts = new int[5];
        if (presentIds.Length != 0) {
            var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
            await using var _1 = dbContext.ConfigureAwait(false);

            var levels = await dbContext.Moods
                .Where(m => m.RoomId == roomId && presentIds.Contains(m.SessionId))
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
}

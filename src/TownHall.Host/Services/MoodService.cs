using Microsoft.EntityFrameworkCore;
using TownHall.Db;

namespace TownHall.Host.Services;

// Frontend mood service: the summary is open to guests; a guest has no own mood and can't set one.
public sealed class MoodService(ServerShared shared, Identity identity)
    : ServerService(shared, identity), IMood
{
    public IAsyncEnumerable<MoodView> GetSummary(string roomId, CancellationToken cancellationToken = default)
        => Stream([$"room:{roomId}", SessionScope], ct => ReadSummary(roomId, ct), cancellationToken);

    public async Task SetMood(Mood_Set command, CancellationToken cancellationToken = default)
    {
        var (roomId, level) = command;
        var userId = await RequireUserId(cancellationToken).ConfigureAwait(false);
        await Shared.Mood.SetMood(new MoodBackend_Set(roomId, userId, level), cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<(MoodView Value, TimeSpan? Wake)> ReadSummary(string roomId, CancellationToken cancellationToken)
    {
        var (summary, nextExpiry) = await Shared.Mood.ReadSummary(roomId, cancellationToken).ConfigureAwait(false);
        var userId = await GetUserId(cancellationToken).ConfigureAwait(false);
        int? ownLevel = null;
        if (userId != null) {
            var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
            await using var _1 = dbContext.ConfigureAwait(false);
            ownLevel = await dbContext.Moods
                .Where(m => m.RoomId == roomId && m.UserId == userId)
                .Select(m => (int?)m.Level)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }
        return (new MoodView(summary, ownLevel), ToWake(nextExpiry));
    }
}

namespace TownHall;

// Backend mood store. ReadSummary aggregates only presently-present users that have a stored mood; the
// frontend adds the caller's own level.
public interface IMoodBackend
{
    Task<(MoodSummary Summary, Moment? NextChange)> ReadSummary(string roomId, CancellationToken cancellationToken = default);

    Task SetMood(MoodBackend_Set command, CancellationToken cancellationToken = default);
}

// ReSharper disable once InconsistentNaming
public sealed record MoodBackend_Set(
    string RoomId,
    string UserId,
    int Level
);

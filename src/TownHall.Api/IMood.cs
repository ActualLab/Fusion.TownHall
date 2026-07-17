namespace TownHall;

public interface IMood
{
    // The room's aggregate mood over presently-present users, plus this session's own level
    IAsyncEnumerable<MoodView> GetSummary(string roomId, CancellationToken cancellationToken = default);

    // Requires room Live. Level in 1..5. Overwrites the previous value.
    Task SetMood(Mood_Set command, CancellationToken cancellationToken = default);
}

// ReSharper disable once InconsistentNaming
public sealed record Mood_Set(
    string RoomId,
    int Level
);

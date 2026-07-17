using MessagePack;

namespace TownHall;

public interface IMood : IComputeService
{
    // Aggregates ONLY presently-present sessions (30 s presence TTL) that have a stored mood
    [ComputeMethod]
    Task<MoodSummary> GetSummary(Session session, string roomId, CancellationToken cancellationToken = default);

    // This session's stored level (1..5) or null - drives button highlight
    [ComputeMethod]
    Task<int?> GetOwn(Session session, string roomId, CancellationToken cancellationToken = default);

    // Requires room Live. Level in 1..5. Overwrites the previous value.
    [CommandHandler]
    Task SetMood(Mood_Set command, CancellationToken cancellationToken = default);
}

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Mood_Set(
    Session Session,
    string RoomId,
    int Level
) : ISessionCommand<Unit>, IDelegatingCommand;

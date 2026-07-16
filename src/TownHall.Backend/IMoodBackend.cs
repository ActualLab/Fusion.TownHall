using MessagePack;

namespace TownHall;

// Backend mood store. GetSummary aggregates only presently-present users that have a stored mood.
public interface IMoodBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<MoodSummary> GetSummary(string roomId, CancellationToken cancellationToken = default);
    [ComputeMethod]
    Task<int?> GetOwn(string roomId, string userId, CancellationToken cancellationToken = default);

    [CommandHandler]
    Task OnSetMood(MoodBackend_Set command, CancellationToken cancellationToken = default);
}

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record MoodBackend_Set(
    string RoomId,
    string UserId,
    int Level
) : ICommand<Unit>, IBackendCommand;

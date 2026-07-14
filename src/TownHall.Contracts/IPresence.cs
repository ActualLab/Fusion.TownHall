using MessagePack;

namespace TownHall;

public interface IPresence : IComputeService
{
    // Sessions whose last OnWatch for this room is <= 30 s old
    [ComputeMethod]
    Task<int> GetAudienceCount(Session session, string roomId, CancellationToken cancellationToken = default);

    // Heartbeat. The client sends it every 15 s while a room page is open,
    // and once immediately on opening the page - regardless of room status.
    [CommandHandler]
    Task OnWatch(Presence_Watch command, CancellationToken cancellationToken = default);
}

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Presence_Watch(
    Session Session,
    string RoomId
) : ISessionCommand<Unit>;

namespace TownHall;

public interface IPresence
{
    // Heartbeat. The client sends it every 15 s while a room page is open,
    // and once immediately on opening the page - regardless of room status.
    // Audience counts surface via RoomView.Stats / RoomCard.
    Task Watch(Presence_Watch command, CancellationToken cancellationToken = default);
}

// ReSharper disable once InconsistentNaming
public sealed record Presence_Watch(
    string RoomId
);

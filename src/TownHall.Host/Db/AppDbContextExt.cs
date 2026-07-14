using Microsoft.EntityFrameworkCore;

namespace TownHall.Host.Db;

public static class AppDbContextExt
{
    public static async Task<DbRoom> GetRoom(
        this AppDbContext dbContext, string roomId, CancellationToken cancellationToken)
        => await dbContext.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Room not found.");

    public static async Task RequireRoomOwner(
        this AppDbContext dbContext, string roomId, string sessionId, CancellationToken cancellationToken)
    {
        var isOwner = await dbContext.RoomOwners
            .AnyAsync(o => o.RoomId == roomId && o.SessionId == sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (!isOwner)
            throw new UnauthorizedAccessException("Only town hall owners can do this.");
    }
}

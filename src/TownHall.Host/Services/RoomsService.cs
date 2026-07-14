using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;
using TownHall.Host.Db;

namespace TownHall.Host.Services;

public class RoomsService(IServiceProvider services) : DbServiceBase<AppDbContext>(services), IRooms
{
    public static readonly TimeSpan ResurrectionGracePeriod = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxDuration = TimeSpan.FromHours(24);
    private const string IdAlphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

    private IDbEntityResolver<string, DbRoom> RoomResolver { get; } = services.DbEntityResolver<string, DbRoom>();

    public virtual async Task<Room?> Get(Session session, string roomId, CancellationToken cancellationToken = default)
        => await GetRoom(roomId, cancellationToken).ConfigureAwait(false);

    public virtual async Task<ImmutableArray<string>> ListActiveIds(Session session, CancellationToken cancellationToken = default)
        => await ListActiveRoomIds(cancellationToken).ConfigureAwait(false);

    public virtual async Task<bool> IsOwner(Session session, string roomId, CancellationToken cancellationToken = default)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        string sessionId = session.Id;
        return await dbContext.RoomOwners
            .AnyAsync(o => o.RoomId == roomId && o.SessionId == sessionId, cancellationToken)
            .ConfigureAwait(false);
    }

    public virtual async Task<string?> GetOwnerToken(Session session, string roomId, CancellationToken cancellationToken = default)
    {
        if (!await IsOwner(session, roomId, cancellationToken).ConfigureAwait(false))
            return null;

        var dbRoom = await RoomResolver.Get(roomId, cancellationToken).ConfigureAwait(false);
        return dbRoom?.OwnerToken;
    }

    public virtual async Task<Room> OnCreate(Rooms_Create command, CancellationToken cancellationToken = default)
    {
        var (session, title, duration, isPrivate) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var roomId = context.Operation.Items.Get<string>("RoomId")!;
            _ = GetRoom(roomId, default);
            _ = ListActiveRoomIds(default);
            _ = IsOwner(session, roomId, default);
            return null!;
        }

        title = title.Trim();
        if (title.Length is < 1 or > 80)
            throw new ArgumentException("Title must be 1..80 characters long.");
        if (duration < MinDuration)
            duration = MinDuration;
        if (duration > MaxDuration)
            duration = MaxDuration;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var id = "th-" + NextRandomString(5);
        while (await dbContext.Rooms.AnyAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false))
            id = "th-" + NextRandomString(5);
        var now = Clocks.SystemClock.Now;
        var closesAt = now + duration;
        var dbRoom = new DbRoom {
            Id = id,
            Title = title,
            OwnerToken = NextRandomString(24),
            IsPrivate = isPrivate,
            CreatedAt = now,
            ClosesAt = closesAt,
        };
        dbContext.Add(dbRoom);
        dbContext.Add(new DbRoomOwner { RoomId = id, SessionId = session.Id });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.Set("RoomId", id);
        return new Room(id, title, now, closesAt, RoomStatus.Paused, isPrivate);
    }

    public virtual async Task OnClaimOwnership(Rooms_ClaimOwnership command, CancellationToken cancellationToken = default)
    {
        var (session, roomId, ownerToken) = command;
        if (Invalidation.IsActive) {
            _ = IsOwner(session, roomId, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(dbRoom.OwnerToken, ownerToken, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Invalid owner token.");

        string sessionId = session.Id;
        var isOwner = await dbContext.RoomOwners
            .AnyAsync(o => o.RoomId == roomId && o.SessionId == sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (isOwner)
            return;

        dbContext.Add(new DbRoomOwner { RoomId = roomId, SessionId = sessionId });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnSetLive(Rooms_SetLive command, CancellationToken cancellationToken = default)
    {
        var (session, roomId, live) = command;
        if (Invalidation.IsActive) {
            _ = GetRoom(roomId, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        await dbContext.RequireRoomOwner(roomId, session.Id, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        if (dbRoom.GetStatus(now) == RoomStatus.Ended)
            throw new InvalidOperationException("This town hall has ended.");
        if (dbRoom.IsLive == live)
            return;

        dbRoom.IsLive = live;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnSetIsPrivate(Rooms_SetIsPrivate command, CancellationToken cancellationToken = default)
    {
        var (session, roomId, isPrivate) = command;
        if (Invalidation.IsActive) {
            _ = GetRoom(roomId, default);
            _ = ListActiveRoomIds(default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        await dbContext.RequireRoomOwner(roomId, session.Id, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        if (dbRoom.GetStatus(now) == RoomStatus.Ended)
            throw new InvalidOperationException("This town hall has ended.");
        if (dbRoom.IsPrivate == isPrivate)
            return;

        dbRoom.IsPrivate = isPrivate;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnSetTitle(Rooms_SetTitle command, CancellationToken cancellationToken = default)
    {
        var (session, roomId, title) = command;
        if (Invalidation.IsActive) {
            _ = GetRoom(roomId, default);
            return;
        }

        title = title.Trim();
        if (title.Length is < 1 or > 80)
            throw new ArgumentException("Title must be 1..80 characters long.");

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        await dbContext.RequireRoomOwner(roomId, session.Id, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        if (dbRoom.GetStatus(now) == RoomStatus.Ended)
            throw new InvalidOperationException("This town hall has ended.");

        dbRoom.Title = title;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnAdjustDuration(Rooms_AdjustDuration command, CancellationToken cancellationToken = default)
    {
        var (session, roomId, delta) = command;
        if (Invalidation.IsActive) {
            _ = GetRoom(roomId, default);
            _ = ListActiveRoomIds(default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        await dbContext.RequireRoomOwner(roomId, session.Id, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        var closesAt = dbRoom.ClosesAt.DefaultKind(DateTimeKind.Utc).ToMoment();
        if (dbRoom.GetStatus(now) == RoomStatus.Ended) {
            // Within the grace period a positive delta resurrects the room,
            // and its closing time drifts to now + delta
            if (delta <= TimeSpan.Zero || now - closesAt > ResurrectionGracePeriod)
                throw new InvalidOperationException("This town hall has ended.");

            dbRoom.ClosesAt = now + delta;
        }
        else {
            var createdAt = dbRoom.CreatedAt.DefaultKind(DateTimeKind.Utc).ToMoment();
            dbRoom.ClosesAt = Moment.Max(now, Moment.Min(createdAt + MaxDuration, closesAt + delta));
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // Protected/internal methods

    [ComputeMethod]
    protected virtual async Task<Room?> GetRoom(string roomId, CancellationToken cancellationToken = default)
    {
        var dbRoom = await RoomResolver.Get(roomId, cancellationToken).ConfigureAwait(false);
        if (dbRoom == null)
            return null;

        var now = Clocks.SystemClock.Now;
        var status = dbRoom.GetStatus(now);
        var closesAt = dbRoom.ClosesAt.DefaultKind(DateTimeKind.Utc).ToMoment();
        // The status flips to Ended on its own at ClosesAt, so this computed must expire then
        if (status != RoomStatus.Ended)
            Computed.GetCurrent().Invalidate(closesAt - now + TimeSpan.FromMilliseconds(100));
        return new Room(dbRoom.Id, dbRoom.Title,
            dbRoom.CreatedAt.DefaultKind(DateTimeKind.Utc), closesAt, status, dbRoom.IsPrivate);
    }

    [ComputeMethod]
    protected virtual async Task<ImmutableArray<string>> ListActiveRoomIds(CancellationToken cancellationToken = default)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var now = Clocks.SystemClock.Now;
        var minClosesAt = now.ToDateTime();
        var rooms = await dbContext.Rooms
            .Where(r => r.ClosesAt > minClosesAt && !r.IsPrivate)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { r.Id, r.ClosesAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        // The earliest ClosesAt is when the first of these rooms drops off the list
        if (rooms.Count != 0) {
            var nextChangeAt = rooms.Min(r => r.ClosesAt).DefaultKind(DateTimeKind.Utc).ToMoment();
            Computed.GetCurrent().Invalidate(nextChangeAt - now + TimeSpan.FromMilliseconds(100));
        }
        return [..rooms.Select(r => r.Id)];
    }

    // Private methods

    private static string NextRandomString(int length)
        => string.Create(length, 0, static (span, _) => {
            foreach (ref var c in span)
                c = IdAlphabet[Random.Shared.Next(IdAlphabet.Length)];
        });
}

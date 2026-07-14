using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;
using TownHall.Db;

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

    public virtual async Task<ImmutableArray<string>> ListActive(Session session, CancellationToken cancellationToken = default)
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
        var (session, title, duration, isPrivate, link, description) = command;
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
        link = NormalizeLink(link);
        description = NormalizeDescription(description);
        if (duration < MinDuration)
            duration = MinDuration;
        if (duration > MaxDuration)
            duration = MaxDuration;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var id = "th-" + NextRandomString(5);
        while (await dbContext.Rooms.AnyAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false))
            id = "th-" + NextRandomString(5);
        var now = Clocks.SystemClock.Now.ToDbPrecision();
        var endsAt = now + duration;
        var dbRoom = new DbRoom {
            Id = id,
            Title = title,
            Link = link,
            Description = description,
            OwnerToken = NextRandomString(24),
            IsPrivate = isPrivate,
            CreatedAt = now,
            EndsAt = endsAt,
            PausedAt = now,  // Created paused / not started; the timer is frozen until first resumed
        };
        dbContext.Add(dbRoom);
        dbContext.Add(new DbRoomOwner { RoomId = id, SessionId = session.Id });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.Set("RoomId", id);
        return new Room(id, title, link, description, now, endsAt, now, RoomStatus.Paused, isPrivate);
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
        var now = Clocks.SystemClock.Now.ToDbPrecision();
        if (dbRoom.GetStatus(now) == RoomStatus.Ended)
            throw new InvalidOperationException("This town hall has ended.");
        var isRunning = dbRoom.PausedAt == null;
        if (live == isRunning)
            return;

        if (live) {
            // Resume: shift EndsAt forward by the paused duration so the frozen remaining time continues
            var pausedAt = dbRoom.PausedAt!.Value.DefaultKind(DateTimeKind.Utc).ToMoment();
            var endsAt = dbRoom.EndsAt.DefaultKind(DateTimeKind.Utc).ToMoment();
            dbRoom.EndsAt = (endsAt + (now - pausedAt)).ToDateTime();
            dbRoom.PausedAt = null;
        }
        else {
            // Pause: freeze the remaining time at now
            dbRoom.PausedAt = now.ToDateTime();
        }
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
        var now = Clocks.SystemClock.Now.ToDbPrecision();
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

        // Title/Link/Description are metadata (not votes or questions), so editing is allowed even after Ended
        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        await dbContext.RequireRoomOwner(roomId, session.Id, cancellationToken).ConfigureAwait(false);
        dbRoom.Title = title;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnSetLink(Rooms_SetLink command, CancellationToken cancellationToken = default)
    {
        var (session, roomId, link) = command;
        if (Invalidation.IsActive) {
            _ = GetRoom(roomId, default);
            return;
        }

        link = NormalizeLink(link);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        await dbContext.RequireRoomOwner(roomId, session.Id, cancellationToken).ConfigureAwait(false);
        dbRoom.Link = link;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnSetDescription(Rooms_SetDescription command, CancellationToken cancellationToken = default)
    {
        var (session, roomId, description) = command;
        if (Invalidation.IsActive) {
            _ = GetRoom(roomId, default);
            return;
        }

        description = NormalizeDescription(description);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        await dbContext.RequireRoomOwner(roomId, session.Id, cancellationToken).ConfigureAwait(false);
        dbRoom.Description = description;
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
        var now = Clocks.SystemClock.Now.ToDbPrecision();
        var endsAt = dbRoom.EndsAt.DefaultKind(DateTimeKind.Utc).ToMoment();
        if (dbRoom.GetStatus(now) == RoomStatus.Ended) {
            // Within the grace period a positive delta resurrects the room as running,
            // and its end time drifts to now + delta
            if (delta <= TimeSpan.Zero || now - endsAt > ResurrectionGracePeriod)
                throw new InvalidOperationException("This town hall has ended.");

            dbRoom.EndsAt = (now + delta).ToDateTime();
            dbRoom.PausedAt = null;
        }
        else {
            // Shift EndsAt relative to the room's own clock (frozen at PausedAt while paused),
            // clamped so the remaining time stays within [0, MaxDuration]
            var refNow = dbRoom.PausedAt is { } p ? p.DefaultKind(DateTimeKind.Utc).ToMoment() : now;
            var createdAt = dbRoom.CreatedAt.DefaultKind(DateTimeKind.Utc).ToMoment();
            dbRoom.EndsAt = Moment.Max(refNow, Moment.Min(createdAt + MaxDuration, endsAt + delta)).ToDateTime();
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

        var now = Clocks.SystemClock.Now.ToDbPrecision();
        var status = dbRoom.GetStatus(now);
        var endsAt = dbRoom.EndsAt.DefaultKind(DateTimeKind.Utc).ToMoment();
        Moment? pausedAt = dbRoom.PausedAt is { } p ? p.DefaultKind(DateTimeKind.Utc).ToMoment() : null;
        // A running hall flips to Ended on its own at EndsAt, so this computed must expire then;
        // a paused hall doesn't change status on its own
        if (status == RoomStatus.Live)
            Computed.GetCurrent().Invalidate(endsAt - now + TimeSpan.FromMilliseconds(100));
        return new Room(dbRoom.Id, dbRoom.Title, dbRoom.Link, dbRoom.Description,
            dbRoom.CreatedAt.DefaultKind(DateTimeKind.Utc), endsAt, pausedAt, status, dbRoom.IsPrivate);
    }

    [ComputeMethod]
    protected virtual async Task<ImmutableArray<string>> ListActiveRoomIds(CancellationToken cancellationToken = default)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var now = Clocks.SystemClock.Now.ToDbPrecision();
        var nowDt = now.ToDateTime();
        // Active = not Ended: paused halls (frozen) plus running halls whose EndsAt is still ahead
        var rooms = await dbContext.Rooms
            .Where(r => !r.IsPrivate && (r.PausedAt != null || r.EndsAt > nowDt))
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { r.Id, r.EndsAt, r.PausedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        // The earliest EndsAt among running halls is when the first one drops off the list
        var runningEnds = rooms.Where(r => r.PausedAt == null).Select(r => r.EndsAt).ToList();
        if (runningEnds.Count != 0) {
            var nextChangeAt = runningEnds.Min().DefaultKind(DateTimeKind.Utc).ToMoment();
            Computed.GetCurrent().Invalidate(nextChangeAt - now + TimeSpan.FromMilliseconds(100));
        }
        return [..rooms.Select(r => r.Id)];
    }

    // Private methods

    private static string NormalizeLink(string link)
    {
        link = link.Trim();
        if (link.Length == 0)
            return "";
        if (link.Length > 500)
            throw new ArgumentException("Link must be at most 500 characters long.");
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Link must be a valid http(s) URL.");

        return link;
    }

    private static string NormalizeDescription(string description)
    {
        // Single paragraph, like a question: line feeds and whitespace runs collapse to single spaces
        description = string.Join(" ", description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (description.Length > 1000)
            throw new ArgumentException("Description must be at most 1000 characters long.");

        return description;
    }

    private static string NextRandomString(int length)
        => string.Create(length, 0, static (span, _) => {
            foreach (ref var c in span)
                c = IdAlphabet[Random.Shared.Next(IdAlphabet.Length)];
        });
}

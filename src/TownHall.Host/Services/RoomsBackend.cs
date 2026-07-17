using Microsoft.EntityFrameworkCore;
using TownHall.Db;

namespace TownHall.Host.Services;

public sealed class RoomsBackend(
    IDbContextFactory<AppDbContext> dbContextFactory, ChangeTracker changes, PresenceStore presence)
    : BackendService(dbContextFactory, changes), IRoomsBackend
{
    public static readonly TimeSpan ResurrectionGracePeriod = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxDuration = TimeSpan.FromHours(24);
    // Ended halls stay in the list this long; the list is capped at the caller's limit (paginated client-side)
    private static readonly TimeSpan RecentWindow = TimeSpan.FromDays(7);
    private const string IdAlphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

    public async Task<(Room? Room, Moment? NextChange)> ReadRoom(string roomId, CancellationToken cancellationToken = default)
    {
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, cancellationToken).ConfigureAwait(false);
        if (dbRoom == null)
            return (null, null);

        var now = Moment.Now.ToDbPrecision();
        var room = ToRoom(dbRoom, now);
        return (room, room.Status == RoomStatus.Live ? room.EndsAt : null);
    }

    public async Task<(ImmutableArray<string> Ids, Moment? NextChange)> ListRoomIds(int limit, CancellationToken cancellationToken = default)
    {
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var now = Moment.Now.ToDbPrecision();
        var recentCutoffDt = (now - RecentWindow).ToDateTime();
        // Active (paused halls or running halls still ahead of now) plus halls that ended within the last
        // week - a running hall's EndsAt is the moment it ended, so EndsAt > cutoff keeps recent ones in
        var rooms = await dbContext.Rooms
            .Where(r => !r.IsPrivate && (r.PausedAt != null || r.EndsAt > recentCutoffDt))
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .Select(r => new { r.Id, r.EndsAt, r.PausedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        // A running hall leaves the list a week after it ends; the earliest such moment is when the set
        // next changes on its own (status flips are handled per-room by ReadRoom, not here)
        var runningEnds = rooms.Where(r => r.PausedAt == null).Select(r => r.EndsAt).ToList();
        Moment? nextChange = runningEnds.Count != 0
            ? runningEnds.Min().DefaultKind(DateTimeKind.Utc).ToMoment() + RecentWindow
            : null;
        return ([..rooms.Select(r => r.Id)], nextChange);
    }

    public async Task<bool> IsOwner(string roomId, string userId, CancellationToken cancellationToken = default)
    {
        if (userId.Length == 0)
            return false;

        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        return await dbContext.RoomOwners
            .AnyAsync(o => o.RoomId == roomId && o.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string?> GetOwnerToken(string roomId, CancellationToken cancellationToken = default)
    {
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        return await dbContext.Rooms
            .Where(r => r.Id == roomId)
            .Select(r => r.OwnerToken)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<RoomStats> ReadStats(string roomId, CancellationToken cancellationToken = default)
    {
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var openCount = await dbContext.Questions.CountAsync(q => q.RoomId == roomId && q.ResolvedAt == null, cancellationToken).ConfigureAwait(false);
        var resolvedCount = await dbContext.Questions.CountAsync(q => q.RoomId == roomId && q.ResolvedAt != null, cancellationToken).ConfigureAwait(false);
        var totalVotes = await dbContext.Votes.LongCountAsync(v => v.RoomId == roomId, cancellationToken).ConfigureAwait(false);
        var audience = presence.Present(roomId).Ids.Length;
        return new RoomStats(openCount, resolvedCount, totalVotes, audience);
    }

    public async Task<(RoomCard? Card, Moment? NextChange)> ReadRoomCard(string roomId, CancellationToken cancellationToken = default)
    {
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, cancellationToken).ConfigureAwait(false);
        if (dbRoom == null)
            return (null, null);

        var now = Moment.Now.ToDbPrecision();
        var room = ToRoom(dbRoom, now);
        var questionCount = await dbContext.Questions.CountAsync(q => q.RoomId == roomId, cancellationToken).ConfigureAwait(false);

        var (presentIds, nextExpiry) = presence.Present(roomId);
        double? averageMood = null;
        if (presentIds.Length != 0) {
            var ids = presentIds.ToArray();
            var levels = await dbContext.Moods
                .Where(m => m.RoomId == roomId && ids.Contains(m.UserId))
                .Select(m => m.Level)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (levels.Count != 0)
                averageMood = levels.Average();
        }

        Moment? nextChange = room.Status == RoomStatus.Live ? room.EndsAt : null;
        if (nextExpiry is { } e)
            nextChange = nextChange is { } nc ? Moment.Min(nc, e) : e;
        return (new RoomCard(room, presentIds.Length, questionCount, averageMood), nextChange);
    }

    public async Task<Room> Create(RoomsBackend_Create command, CancellationToken cancellationToken = default)
    {
        var (ownerUserId, title, duration, isPrivate, link, description) = command;
        title = title.Trim();
        if (title.Length is < 1 or > 80)
            throw new ArgumentException("Title must be 1..80 characters long.");
        link = NormalizeLink(link);
        description = NormalizeDescription(description);
        if (duration < MinDuration)
            duration = MinDuration;
        if (duration > MaxDuration)
            duration = MaxDuration;

        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var id = "th-" + NextRandomString(5);
        while (await dbContext.Rooms.AnyAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false))
            id = "th-" + NextRandomString(5);
        var now = Moment.Now.ToDbPrecision();
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
        dbContext.Add(new DbRoomOwner { RoomId = id, UserId = ownerUserId });
        await SaveAndNotify(dbContext, cancellationToken, "lobby");
        return new Room(id, title, link, description, now, endsAt, now, RoomStatus.Paused, isPrivate);
    }

    public async Task ClaimOwnership(RoomsBackend_ClaimOwnership command, CancellationToken cancellationToken = default)
    {
        var (roomId, userId, ownerToken) = command;
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(dbRoom.OwnerToken, ownerToken, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Invalid owner token.");

        var isOwner = await dbContext.RoomOwners
            .AnyAsync(o => o.RoomId == roomId && o.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (isOwner)
            return;

        dbContext.Add(new DbRoomOwner { RoomId = roomId, UserId = userId });
        await SaveAndNotify(dbContext, cancellationToken, $"room:{roomId}");
    }

    public async Task SetLive(RoomsBackend_SetLive command, CancellationToken cancellationToken = default)
    {
        var (roomId, live) = command;
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        var now = Moment.Now.ToDbPrecision();
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
        await SaveAndNotify(dbContext, cancellationToken, $"room:{roomId}");
    }

    public async Task SetIsPrivate(RoomsBackend_SetIsPrivate command, CancellationToken cancellationToken = default)
    {
        var (roomId, isPrivate) = command;
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        var now = Moment.Now.ToDbPrecision();
        if (dbRoom.GetStatus(now) == RoomStatus.Ended)
            throw new InvalidOperationException("This town hall has ended.");
        if (dbRoom.IsPrivate == isPrivate)
            return;

        dbRoom.IsPrivate = isPrivate;
        await SaveAndNotify(dbContext, cancellationToken, $"room:{roomId}", "lobby");
    }

    public async Task SetTitle(RoomsBackend_SetTitle command, CancellationToken cancellationToken = default)
    {
        var (roomId, title) = command;
        title = title.Trim();
        if (title.Length is < 1 or > 80)
            throw new ArgumentException("Title must be 1..80 characters long.");

        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        // Title/Link/Description are metadata (not votes or questions), so editing is allowed even after Ended
        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        dbRoom.Title = title;
        await SaveAndNotify(dbContext, cancellationToken, $"room:{roomId}");
    }

    public async Task SetLink(RoomsBackend_SetLink command, CancellationToken cancellationToken = default)
    {
        var (roomId, link) = command;
        link = NormalizeLink(link);

        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        dbRoom.Link = link;
        await SaveAndNotify(dbContext, cancellationToken, $"room:{roomId}");
    }

    public async Task SetDescription(RoomsBackend_SetDescription command, CancellationToken cancellationToken = default)
    {
        var (roomId, description) = command;
        description = NormalizeDescription(description);

        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        dbRoom.Description = description;
        await SaveAndNotify(dbContext, cancellationToken, $"room:{roomId}");
    }

    public async Task AdjustDuration(RoomsBackend_AdjustDuration command, CancellationToken cancellationToken = default)
    {
        var (roomId, delta) = command;
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        var now = Moment.Now.ToDbPrecision();
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
        await SaveAndNotify(dbContext, cancellationToken, $"room:{roomId}", "lobby");
    }

    // Private methods

    private static Room ToRoom(DbRoom dbRoom, Moment now)
    {
        var status = dbRoom.GetStatus(now);
        var endsAt = dbRoom.EndsAt.DefaultKind(DateTimeKind.Utc).ToMoment();
        Moment? pausedAt = dbRoom.PausedAt is { } p ? p.DefaultKind(DateTimeKind.Utc).ToMoment() : null;
        return new Room(dbRoom.Id, dbRoom.Title, dbRoom.Link, dbRoom.Description,
            dbRoom.CreatedAt.DefaultKind(DateTimeKind.Utc).ToMoment(), endsAt, pausedAt, status, dbRoom.IsPrivate);
    }

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

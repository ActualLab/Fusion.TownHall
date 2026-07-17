using Microsoft.EntityFrameworkCore;
using TownHall.Db;

namespace TownHall.Host.Services;

public sealed class UsersBackend(IDbContextFactory<AppDbContext> dbContextFactory, ChangeTracker changes)
    : BackendService(dbContextFactory, changes), IUsersBackend
{
    private const string IdAlphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

    public async Task<UserFull?> Get(string userId, CancellationToken cancellationToken = default)
    {
        // Anonymous authors have no DB row - their name is generated from the (user, room)-derived id.
        if (AnonId.Is(userId))
            return new UserFull(userId, NameGenerator.New(userId), default);

        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);
        return dbUser == null
            ? null
            : new UserFull(dbUser.Id, dbUser.Name, dbUser.CreatedAt.DefaultKind(DateTimeKind.Utc));
    }

    // Resolves the display name for a batch of author ids (a mix of real user ids and anon ids).
    public async Task<Dictionary<string, string>> GetNames(
        IReadOnlyCollection<string> authorIds, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var realIds = authorIds.Where(id => !AnonId.Is(id)).Distinct().ToArray();
        if (realIds.Length != 0) {
            var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
            await using var _1 = dbContext.ConfigureAwait(false);
            var names = await dbContext.Users
                .Where(u => realIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Name })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var n in names)
                result[n.Id] = n.Name;
        }
        foreach (var id in authorIds)
            result.TryAdd(id, NameGenerator.New(id)); // anon ids + any missing real id fall back to a generated name

        return result;
    }

    public async Task<string?> GetUserIdBySession(string sessionId, CancellationToken cancellationToken = default)
    {
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        return await dbContext.SessionUsers
            .Where(s => s.SessionId == sessionId)
            .Select(s => s.UserId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<StoredCredential?> GetCredential(string credentialId, CancellationToken cancellationToken = default)
    {
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var c = await dbContext.PasskeyCredentials
            .FirstOrDefaultAsync(x => x.CredentialId == credentialId, cancellationToken)
            .ConfigureAwait(false);
        return c == null ? null : new StoredCredential(c.CredentialId, c.UserId, c.PublicKey, c.SignCount, c.UserHandle);
    }

    public async Task<string> Create(UsersBackend_Create command, CancellationToken cancellationToken = default)
    {
        var name = command.Name.Trim();
        if (name.Length is < 1 or > 30)
            name = NameGenerator.New(NextRandomString(8));

        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var id = "u-" + NextRandomString(10);
        while (await dbContext.Users.AnyAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false))
            id = "u-" + NextRandomString(10);
        dbContext.Add(new DbUser {
            Id = id,
            Name = name,
            CreatedAt = Moment.Now.ToDbPrecision().ToDateTime(),
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return id;
    }

    public async Task SetName(UsersBackend_SetName command, CancellationToken cancellationToken = default)
    {
        var (userId, name) = command;
        name = name.Trim();
        if (name.Length is < 1 or > 30)
            throw new ArgumentException("Name must be 1..30 characters long.");

        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        // Rooms where this user authored (their name shows there) + sessions linked to them (own-name views),
        // read before saving so the notify set is built even if SaveChanges then throws.
        var roomIds = await dbContext.Questions
            .Where(q => q.AuthorId == userId).Select(q => q.RoomId).Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var sessionIds = await dbContext.SessionUsers
            .Where(s => s.UserId == userId).Select(s => s.SessionId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var dbUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("User not found.");
        dbUser.Name = name;

        var scopes = new List<string>(roomIds.Count + sessionIds.Count);
        scopes.AddRange(roomIds.Select(r => $"room:{r}"));
        scopes.AddRange(sessionIds.Select(s => $"session:{s}"));
        await SaveAndNotify(dbContext, cancellationToken, [..scopes]);
    }

    public async Task LinkSession(UsersBackend_LinkSession command, CancellationToken cancellationToken = default)
    {
        var (sessionId, userId) = command;
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbLink = await dbContext.SessionUsers
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken).ConfigureAwait(false);
        if (dbLink == null)
            dbContext.Add(new DbSessionUser { SessionId = sessionId, UserId = userId });
        else
            dbLink.UserId = userId;
        await SaveAndNotify(dbContext, cancellationToken, $"session:{sessionId}");
    }

    public async Task UnlinkSession(UsersBackend_UnlinkSession command, CancellationToken cancellationToken = default)
    {
        var sessionId = command.SessionId;
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbLink = await dbContext.SessionUsers
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken).ConfigureAwait(false);
        if (dbLink != null)
            dbContext.Remove(dbLink);
        await SaveAndNotify(dbContext, cancellationToken, $"session:{sessionId}");
    }

    public async Task AddCredential(UsersBackend_AddCredential command, CancellationToken cancellationToken = default)
    {
        var (credentialId, userId, publicKey, signCount, userHandle) = command;
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        dbContext.Add(new DbPasskeyCredential {
            CredentialId = credentialId,
            UserId = userId,
            PublicKey = publicKey,
            SignCount = signCount,
            UserHandle = userHandle,
            CreatedAt = Moment.Now.ToDbPrecision().ToDateTime(),
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateSignCount(UsersBackend_UpdateSignCount command, CancellationToken cancellationToken = default)
    {
        var (credentialId, signCount) = command;
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbCredential = await dbContext.PasskeyCredentials
            .FirstOrDefaultAsync(c => c.CredentialId == credentialId, cancellationToken).ConfigureAwait(false);
        if (dbCredential == null)
            return;

        dbCredential.SignCount = signCount;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private static string NextRandomString(int length)
        => string.Create(length, 0, static (span, _) => {
            foreach (ref var c in span)
                c = IdAlphabet[Random.Shared.Next(IdAlphabet.Length)];
        });
}

using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;
using TownHall.Db;

namespace TownHall.Host.Services;

public class UsersBackend(IServiceProvider services) : DbServiceBase<AppDbContext>(services), IUsersBackend
{
    private const string IdAlphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

    private IDbEntityResolver<string, DbUser> UserResolver { get; } = services.DbEntityResolver<string, DbUser>();

    public virtual async Task<UserFull?> Get(string userId, CancellationToken cancellationToken = default)
    {
        // Anonymous authors have no DB row - their name is generated from the (user, room)-derived id.
        if (AnonId.Is(userId))
            return new UserFull(userId, NameGenerator.New(userId), default);

        var dbUser = await UserResolver.Get(userId, cancellationToken).ConfigureAwait(false);
        return dbUser == null
            ? null
            : new UserFull(dbUser.Id, dbUser.Name, dbUser.CreatedAt.DefaultKind(DateTimeKind.Utc));
    }

    public virtual async Task<string?> GetUserIdBySession(string sessionId, CancellationToken cancellationToken = default)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        return await dbContext.SessionUsers
            .Where(s => s.SessionId == sessionId)
            .Select(s => s.UserId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string?> GetUserIdByCredential(string credentialId, CancellationToken cancellationToken = default)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        return await dbContext.PasskeyCredentials
            .Where(c => c.CredentialId == credentialId)
            .Select(c => c.UserId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<StoredCredential?> GetCredential(string credentialId, CancellationToken cancellationToken = default)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var c = await dbContext.PasskeyCredentials
            .FirstOrDefaultAsync(x => x.CredentialId == credentialId, cancellationToken)
            .ConfigureAwait(false);
        return c == null ? null : new StoredCredential(c.CredentialId, c.UserId, c.PublicKey, c.SignCount, c.UserHandle);
    }

    public async Task<StoredCredential[]> ListCredentials(string userId, CancellationToken cancellationToken = default)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var credentials = await dbContext.PasskeyCredentials
            .Where(c => c.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [
            ..credentials.Select(c =>
                new StoredCredential(c.CredentialId, c.UserId, c.PublicKey, c.SignCount, c.UserHandle))
        ];
    }

    public virtual async Task<string> Create(UsersBackend_Create command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return null!;

        var name = command.Name.Trim();
        if (name.Length is < 1 or > 30)
            name = NameGenerator.New(NextRandomString(8));

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var id = "u-" + NextRandomString(10);
        while (await dbContext.Users.AnyAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false))
            id = "u-" + NextRandomString(10);
        dbContext.Add(new DbUser {
            Id = id,
            Name = name,
            CreatedAt = Clocks.SystemClock.Now.ToDbPrecision().ToDateTime(),
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return id;
    }

    public virtual async Task SetName(UsersBackend_SetName command, CancellationToken cancellationToken = default)
    {
        var (userId, name) = command;
        if (Invalidation.IsActive) {
            _ = Get(userId, default);
            return;
        }

        name = name.Trim();
        if (name.Length is < 1 or > 30)
            throw new ArgumentException("Name must be 1..30 characters long.");

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("User not found.");
        dbUser.Name = name;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task LinkSession(UsersBackend_LinkSession command, CancellationToken cancellationToken = default)
    {
        var (sessionId, userId) = command;
        if (Invalidation.IsActive) {
            _ = GetUserIdBySession(sessionId, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbLink = await dbContext.SessionUsers
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken).ConfigureAwait(false);
        if (dbLink == null)
            dbContext.Add(new DbSessionUser { SessionId = sessionId, UserId = userId });
        else
            dbLink.UserId = userId;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task UnlinkSession(UsersBackend_UnlinkSession command, CancellationToken cancellationToken = default)
    {
        var sessionId = command.SessionId;
        if (Invalidation.IsActive) {
            _ = GetUserIdBySession(sessionId, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbLink = await dbContext.SessionUsers
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken).ConfigureAwait(false);
        if (dbLink == null)
            return;

        dbContext.Remove(dbLink);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task AddCredential(UsersBackend_AddCredential command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return;

        var (credentialId, userId, publicKey, signCount, userHandle) = command;
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        dbContext.Add(new DbPasskeyCredential {
            CredentialId = credentialId,
            UserId = userId,
            PublicKey = publicKey,
            SignCount = signCount,
            UserHandle = userHandle,
            CreatedAt = Clocks.SystemClock.Now.ToDbPrecision().ToDateTime(),
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task UpdateSignCount(UsersBackend_UpdateSignCount command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return;

        var (credentialId, signCount) = command;
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
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

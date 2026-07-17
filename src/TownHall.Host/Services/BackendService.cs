using Microsoft.EntityFrameworkCore;
using TownHall.Db;

namespace TownHall.Host.Services;

// Base for the id-based backend services: they do the DB work and fire change notifications, but know
// nothing about the calling session (that's the frontend services' job). Registered as singletons.
public abstract class BackendService(IDbContextFactory<AppDbContext> dbContextFactory, ChangeTracker changes)
{
    protected ChangeTracker Changes { get; } = changes;

    protected Task<AppDbContext> CreateDbContext(CancellationToken cancellationToken)
        => dbContextFactory.CreateDbContextAsync(cancellationToken);

    // Persists pending changes and notifies the given scopes (see ServerService for the over-notify
    // rationale: the notify fires even if SaveChanges throws, because the write may still have committed).
    protected async Task SaveAndNotify(AppDbContext db, CancellationToken cancellationToken, params string[] scopes)
    {
        try {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally {
            Changes.Notify(scopes);
        }
    }
}

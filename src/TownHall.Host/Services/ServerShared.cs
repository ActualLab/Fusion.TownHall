using Fido2NetLib;
using Microsoft.EntityFrameworkCore;
using TownHall.Db;

namespace TownHall.Host.Services;

// Process-wide singletons shared by every per-connection frontend service instance: the DB factory,
// the reactivity core, the presence store, the id-based backend services the frontends delegate to,
// and the passkey (WebAuthn) config + challenge store.
public sealed class ServerShared(
    IDbContextFactory<AppDbContext> dbContextFactory,
    ChangeTracker changes,
    PresenceStore presence,
    IUsersBackend users,
    IRoomsBackend rooms,
    IQuestionsBackend questions,
    IMoodBackend mood,
    IPresenceBackend presenceBackend,
    IRoomStatsBackend roomStats,
    Fido2Configuration fido2Config,
    PasskeyChallengeStore challenges)
{
    public IDbContextFactory<AppDbContext> DbContextFactory { get; } = dbContextFactory;
    public ChangeTracker Changes { get; } = changes;
    public PresenceStore Presence { get; } = presence;
    public IUsersBackend Users { get; } = users;
    public IRoomsBackend Rooms { get; } = rooms;
    public IQuestionsBackend Questions { get; } = questions;
    public IMoodBackend Mood { get; } = mood;
    public IPresenceBackend PresenceBackend { get; } = presenceBackend;
    public IRoomStatsBackend RoomStats { get; } = roomStats;
    public Fido2Configuration Fido2Config { get; } = fido2Config;
    public PasskeyChallengeStore Challenges { get; } = challenges;
}

using Microsoft.EntityFrameworkCore;

namespace TownHall.Db;

public class AppDbContext(DbContextOptions options) : DbContext(options)
{
    // The local docker-compose Postgres; HostSettings.PostgreSql and the design-time
    // factory both default to it.
    public const string DefaultConnectionString =
        "Server=127.0.0.1;Database=townhall;Port=5432;User Id=postgres;Password=postgres";

    public DbSet<DbRoom> Rooms { get; protected set; } = null!;
    public DbSet<DbRoomOwner> RoomOwners { get; protected set; } = null!;
    public DbSet<DbUser> Users { get; protected set; } = null!;
    public DbSet<DbPasskeyCredential> PasskeyCredentials { get; protected set; } = null!;
    public DbSet<DbSessionUser> SessionUsers { get; protected set; } = null!;
    public DbSet<DbQuestion> Questions { get; protected set; } = null!;
    public DbSet<DbVote> Votes { get; protected set; } = null!;
    public DbSet<DbMood> Moods { get; protected set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<DbRoomOwner>().HasKey(o => new { o.RoomId, o.UserId });
        modelBuilder.Entity<DbPasskeyCredential>().HasIndex(c => c.UserId);
        modelBuilder.Entity<DbQuestion>().HasIndex(q => new { q.RoomId, q.Index }).IsUnique();
        modelBuilder.Entity<DbVote>().HasKey(v => new { v.RoomId, v.QuestionIndex, v.UserId });
        modelBuilder.Entity<DbMood>().HasKey(m => new { m.RoomId, m.UserId });
    }
}

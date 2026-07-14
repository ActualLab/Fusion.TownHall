using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;
using Microsoft.EntityFrameworkCore;

namespace TownHall.Db;

public class AppDbContext(DbContextOptions options) : DbContextBase(options)
{
    // The local docker-compose Postgres; HostSettings.PostgreSql and the design-time
    // factory both default to it.
    public const string DefaultConnectionString =
        "Server=localhost;Database=townhall;Port=5432;User Id=postgres;Password=postgres";

    public DbSet<DbRoom> Rooms { get; protected set; } = null!;
    public DbSet<DbRoomOwner> RoomOwners { get; protected set; } = null!;
    public DbSet<DbParticipant> Participants { get; protected set; } = null!;
    public DbSet<DbQuestion> Questions { get; protected set; } = null!;
    public DbSet<DbVote> Votes { get; protected set; } = null!;
    public DbSet<DbMood> Moods { get; protected set; } = null!;

    // ActualLab.Fusion.EntityFramework.Operations tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;
    public DbSet<DbEvent> Events { get; protected set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<DbRoomOwner>().HasKey(o => new { o.RoomId, o.SessionId });
        modelBuilder.Entity<DbQuestion>().HasIndex(q => new { q.RoomId, q.Index }).IsUnique();
        modelBuilder.Entity<DbVote>().HasKey(v => new { v.RoomId, v.QuestionIndex, v.SessionId });
        modelBuilder.Entity<DbMood>().HasKey(m => new { m.RoomId, m.SessionId });
    }
}

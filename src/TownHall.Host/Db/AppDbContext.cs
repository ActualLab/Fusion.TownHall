using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;
using Microsoft.EntityFrameworkCore;

namespace TownHall.Host.Db;

public class AppDbContext(DbContextOptions options) : DbContextBase(options)
{
    // ActualLab.Fusion.EntityFramework.Operations tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;
    public DbSet<DbEvent> Events { get; protected set; } = null!;
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TownHall.Db;

/// <summary>
/// Lets "dotnet ef migrations ..." create <see cref="AppDbContext"/>
/// without spinning up the whole host.
/// </summary>
public sealed class DesignTimeAppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(AppDbContext.DefaultConnectionString)
            .Options;
        return new AppDbContext(options);
    }
}

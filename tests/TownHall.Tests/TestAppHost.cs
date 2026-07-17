using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using TownHall.Db;
using TownHall.Host.Services;

namespace TownHall.Tests;

/// <summary>
/// Per-test-class app host: the shared server singletons with a unique Postgres DB
/// (requires the docker-compose Postgres). Tests call the server-side services directly,
/// bound to a chosen session.
/// </summary>
public sealed class TestAppHost : IAsyncLifetime
{
    private ServiceProvider _services = null!;

    public string ConnectionString { get; } = new NpgsqlConnectionStringBuilder(AppDbContext.DefaultConnectionString) {
        Database = $"townhall_tests_{Guid.NewGuid():N}",
    }.ConnectionString;
    public ServerShared Shared { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        ConfigureServices(services, ConnectionString);
        _services = services.BuildServiceProvider();

        var dbContextFactory = _services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync())
            await dbContext.Database.MigrateAsync();
        Shared = _services.GetRequiredService<ServerShared>();
    }

    public async Task DisposeAsync()
    {
        var dbContextFactory = _services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync())
            await dbContext.Database.EnsureDeletedAsync();
        await _services.DisposeAsync();
    }

    public SessionServices For(string sessionId)
        => new(Shared, Identity.Of(sessionId));

    // Private methods

    private static void ConfigureServices(IServiceCollection services, string connectionString)
    {
        // Mirrors the service configuration in TownHall.Host/Program.cs
        services.AddLogging();
        services.AddDbContextFactory<AppDbContext>(o => o
            .UseNpgsql(connectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddSingleton<ChangeTracker>();
        services.AddSingleton<PresenceStore>();
        // Backend (local); tests sign in via the backend directly
        services.AddSingleton<IUsersBackend, UsersBackend>();
        services.AddSingleton<IRoomsBackend, RoomsBackend>();
        services.AddSingleton<IQuestionsBackend, QuestionsBackend>();
        services.AddSingleton<IRoomStatsBackend, RoomStatsBackend>();
        services.AddSingleton<IPresenceBackend, PresenceBackend>();
        services.AddSingleton<IMoodBackend, MoodBackend>();
        services.AddSingleton<ServerShared>();
        // Passkey infrastructure - unused by tests, but ServerShared requires it
        services.AddSingleton<PasskeyChallengeStore>();
        services.AddSingleton(new Fido2NetLib.Fido2Configuration {
            ServerDomain = "localhost", ServerName = "TownHall",
            Origins = new HashSet<string>(StringComparer.Ordinal) { "http://localhost:5136" },
        });
    }
}

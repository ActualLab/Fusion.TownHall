using System.Data;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Npgsql;
using ActualLab.Fusion.EntityFramework.Operations;
using ActualLab.Fusion.Server;
using ActualLab.Rpc;
using ActualLab.Rpc.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TownHall.Db;
using TownHall.Host.Services;

namespace TownHall.Tests;

/// <summary>
/// Per-test-class app host: the server web host on a random port with a unique Postgres DB
/// (requires the docker-compose Postgres), plus a Fusion RPC client container connected to it.
/// </summary>
public sealed class TestAppHost : IAsyncLifetime
{
    private ServiceProvider? _clientServices;

    public string ConnectionString { get; } = new NpgsqlConnectionStringBuilder(AppDbContext.DefaultConnectionString) {
        Database = $"fusion_townhall_tests_{Guid.NewGuid():N}",
    }.ConnectionString;
    public WebApplication App { get; private set; } = null!;
    public string BaseUrl { get; private set; } = "";
    public IServiceProvider Services => App.Services;
    public IServiceProvider ClientServices => _clientServices ??= BuildClientServices();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        ConfigureServices(builder.Services, ConnectionString);
        App = builder.Build();
        App.UseWebSockets();
        App.MapRpcWebSocketServer();

        var dbContextFactory = Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync())
            await dbContext.Database.MigrateAsync();
        await App.StartAsync();
        BaseUrl = App.Urls.First();
    }

    public async Task DisposeAsync()
    {
        if (_clientServices != null)
            await _clientServices.DisposeAsync();

        var dbContextFactory = Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync())
            await dbContext.Database.EnsureDeletedAsync();
        await App.StopAsync();
        await App.DisposeAsync();
    }

    // Private methods

    private static void ConfigureServices(IServiceCollection services, string connectionString)
    {
        // Mirrors the service configuration in TownHall.Host/Program.cs,
        // including the statics ClientStartup.ConfigureSharedServices sets
        RpcSerializationFormatResolver.Default = new("msgpack6c");
        DbOperationScope.Options.DefaultIsolationLevel = IsolationLevel.RepeatableRead;
        services.AddDbContextServices<AppDbContext>(db => {
            db.AddOperations(operations => operations.AddNpgsqlOperationLogWatcher());
            db.AddEntityResolver<string, DbRoom>();
            db.AddEntityResolver<string, DbParticipant>();
            db.AddEntityResolver<string, DbQuestion>();
            // ReSharper disable once VariableHidesOuterVariable
            db.Services.AddTransientDbContextFactory<AppDbContext>((c, db) => {
                db.UseNpgsql(connectionString, npgsql => {
                    npgsql.EnableRetryOnFailure(0);
                });
                db.UseNpgsqlHintFormatter();
            });
        });

        var fusion = services.AddFusion(RpcServiceMode.Server, true);
        fusion.AddWebServer();
        fusion.AddOperationReprocessor();
        fusion.AddServer<IParticipants, ParticipantsService>();
        fusion.AddServer<IRooms, RoomsService>();
        fusion.AddServer<IQuestions, QuestionsService>();
        fusion.AddServer<IRoomStats, RoomStatsService>();
        fusion.AddServer<IPresence, PresenceService>();
        fusion.AddServer<IMood, MoodService>();
    }

    private ServiceProvider BuildClientServices()
    {
        // Mirrors the client configuration in TownHall.UI/ClientStartup.cs
        var services = new ServiceCollection();
        services.AddLogging();
        var fusion = services.AddFusion();
        fusion.Rpc.AddWebSocketClient(BaseUrl);
        fusion.AddClient<IParticipants>();
        fusion.AddClient<IRooms>();
        fusion.AddClient<IQuestions>();
        fusion.AddClient<IRoomStats>();
        fusion.AddClient<IPresence>();
        fusion.AddClient<IMood>();
        return services.BuildServiceProvider();
    }
}

using System.Data;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;
using ActualLab.Fusion.Server;
using ActualLab.Interception;
using ActualLab.IO;
using ActualLab.Rpc;
using ActualLab.Rpc.Server;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration.Memory;
using TownHall;
using TownHall.Host;
using TownHall.Host.Components.Pages;
using TownHall.Host.Db;
using TownHall.Host.Services;
using TownHall.UI;

// IComputeService validation should be off in release
#if !DEBUG
Interceptor.Options.Defaults.IsValidationEnabled = false;
#endif

var builder = WebApplication.CreateBuilder(args);
var env = builder.Environment;
var cfg = builder.Configuration;
var hostSettings = cfg.GetSettings<HostSettings>();

cfg.Sources.Insert(0, new MemoryConfigurationSource() {
    InitialData = new Dictionary<string, string>(StringComparer.Ordinal) {
        { WebHostDefaults.ServerUrlsKey, $"http://localhost:{hostSettings.Port ?? 5136}" }, // Override default server URLs
    }!
});

// Configure services
var services = builder.Services;
ConfigureLogging();
ConfigureServices();
builder.WebHost.UseDefaultServiceProvider((ctx, options) => {
    if (ctx.HostingEnvironment.IsDevelopment()) {
        options.ValidateScopes = true;
        options.ValidateOnBuild = true;
    }
});

// Build & configure app
var app = builder.Build();
StaticLog.Factory = app.Services.LoggerFactory();
ConfigureApp();

// Ensure the DB is created
var dbContextFactory = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
await using (var dbContext = await dbContextFactory.CreateDbContextAsync()) {
    if (hostSettings.MustRecreateDb)
        await dbContext.Database.EnsureDeletedAsync();
    await dbContext.Database.EnsureCreatedAsync();
}

// Run the app
await app.RunAsync();
return;

// Helpers

void ConfigureLogging()
{
    services.AddLogging(logging => {
        // Use appsettings.*.json to change log filters
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    });
}

void ConfigureServices()
{
    services.AddSingleton(hostSettings);

    // DbContext & related services
    DbOperationScope.Options.DefaultIsolationLevel = IsolationLevel.RepeatableRead;
    services.AddDbContextServices<AppDbContext>(db => {
        db.AddOperations(operations => {
            operations.ConfigureOperationLogReader(_ => new() {
                CheckPeriod = TimeSpan.FromSeconds(env.IsDevelopment() ? 60 : 5),
            });
            operations.ConfigureEventLogReader(_ => new() {
                CheckPeriod = TimeSpan.FromSeconds(env.IsDevelopment() ? 60 : 5),
            });
            operations.AddFileSystemOperationLogWatcher();
        });
        // Batched by-key lookups for compute-method reads
        db.AddEntityResolver<string, DbRoom>();
        db.AddEntityResolver<string, DbParticipant>();
        db.AddEntityResolver<string, DbQuestion>();
        // ReSharper disable once VariableHidesOuterVariable
        db.Services.AddTransientDbContextFactory<AppDbContext>((c, db) => {
            var appTempDir = FilePath.GetApplicationTempDirectory("", true);
            var dbPath = appTempDir & "TownHall_v1.db";
            db.UseSqlite($"Data Source={dbPath}");
            if (env.IsDevelopment())
                db.EnableSensitiveDataLogging();
        });
    });

    // Fusion services
    var fusion = services.AddFusion(RpcServiceMode.Server, true);
    fusion.AddWebServer();
    fusion.AddOperationReprocessor();
    fusion.AddServer<IParticipants, ParticipantsService>();
    fusion.AddServer<IRooms, RoomsService>();
    fusion.AddServer<IQuestions, QuestionsService>();
    fusion.AddServer<IRoomStats, RoomStatsService>();
    fusion.AddServer<IPresence, PresenceService>();
    fusion.AddServer<IMood, MoodService>();

    // Web
    services.AddServerSideBlazor(o => o.DetailedErrors = true);
    services.AddRazorComponents()
        .AddInteractiveServerComponents()
        .AddInteractiveWebAssemblyComponents();

    // Shared services; fusion.AddBlazor() inside must follow services.AddServerSideBlazor()
    ClientStartup.ConfigureSharedServices(services);
}

void ConfigureApp()
{
    StaticWebAssetsLoader.UseStaticWebAssets(env, cfg);
    if (app.Environment.IsDevelopment()) {
        app.UseWebAssemblyDebugging();
    }
    else {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }
    app.UseWebSockets(new WebSocketOptions() {
        KeepAliveInterval = TimeSpan.FromSeconds(30),
    });
    app.UseFusionSession();
    app.UseRouting();
    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    app.UseAntiforgery();

    // Razor components
    app.MapStaticAssets();
    app.MapRazorComponents<_HostPage>()
        .AddInteractiveServerRenderMode()
        .AddInteractiveWebAssemblyRenderMode()
        .AddAdditionalAssemblies(typeof(App).Assembly);

    // Fusion endpoints
    app.MapRpcWebSocketServer();
    app.MapFusionRenderModeEndpoints();
}

using System.Data;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Npgsql;
using ActualLab.Fusion.EntityFramework.Operations;
using ActualLab.Fusion.Server;
using ActualLab.Interception;
using ActualLab.Rpc;
using ActualLab.Rpc.Server;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration.Memory;
using TownHall;
using TownHall.Host;
using TownHall.Host.Components.Pages;
using TownHall.Db;
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

// OpenTelemetry (logs/traces/metrics) streams to the Aspire dashboard when run under the AppHost
builder.AddServiceDefaults();

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

// Migrate the DB to the latest schema (see src/TownHall.Db/Migrations)
var dbContextFactory = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
await using (var dbContext = await dbContextFactory.CreateDbContextAsync()) {
    if (hostSettings.MustRecreateDb)
        await dbContext.Database.EnsureDeletedAsync();
    await dbContext.Database.MigrateAsync();
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
            operations.AddNpgsqlOperationLogWatcher();
        });
        // Batched by-key lookups for compute-method reads
        db.AddEntityResolver<string, DbRoom>();
        db.AddEntityResolver<string, DbUser>();
        db.AddEntityResolver<string, DbQuestion>();
        // ReSharper disable once VariableHidesOuterVariable
        db.Services.AddTransientDbContextFactory<AppDbContext>((c, db) => {
            db.UseNpgsql(hostSettings.PostgreSql, npgsql => {
                npgsql.EnableRetryOnFailure(0);
            });
            db.UseNpgsqlHintFormatter();
            db.AddInterceptors(new DbMetrics()); // Query + fetched-row counters (TownHall.Db meter)
            if (env.IsDevelopment())
                db.EnableSensitiveDataLogging();
        });
    });

    // Fusion services
    var fusion = services.AddFusion(RpcServiceMode.Server, true);
    fusion.AddWebServer();
    fusion.AddOperationReprocessor();

    // Backend services - local (not RPC-exposed); they do the work on ids and assume the caller
    // is already authorized. The frontend services above are what RPC exposes.
    fusion.AddComputeService<IUsersBackend, UsersBackend>();
    fusion.AddComputeService<IRoomsBackend, RoomsBackend>();
    fusion.AddComputeService<IQuestionsBackend, QuestionsBackend>();
    fusion.AddComputeService<IRoomStatsBackend, RoomStatsBackend>();
    fusion.AddComputeService<IPresenceBackend, PresenceBackend>();
    fusion.AddComputeService<IMoodBackend, MoodBackend>();

    // Frontend services - RPC-exposed; resolve Session -> user, check permissions, then delegate.
    fusion.AddServer<IUsers, UsersService>();
    fusion.AddServer<IAuth, AuthService>();
    fusion.AddServer<IRooms, RoomsService>();
    fusion.AddServer<IQuestions, QuestionsService>();
    fusion.AddServer<IRoomStats, RoomStatsService>();
    fusion.AddServer<IPresence, PresenceService>();
    fusion.AddServer<IMood, MoodService>();

    // Passkey (WebAuthn) infrastructure
    services.AddSingleton<PasskeyChallengeStore>();
    services.AddFido2(fido2 => {
        fido2.ServerDomain = hostSettings.PasskeyRpId;
        fido2.ServerName = "TownHall";
        fido2.Origins = hostSettings.PasskeyOrigins.ToHashSet(StringComparer.Ordinal);
    });

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

    // Dev-only sign-in without a passkey - for automated/manual testing of the signed-in flows.
    // Gated to Development; never mapped in production.
    if (env.IsDevelopment() || hostSettings.EnableDevSignIn) {
        app.MapPost("/dev/signin", async (ISessionResolver sessionResolver, ICommander commander, string? name) => {
            var session = await sessionResolver.GetSession();
            var userId = await commander.Call(new UsersBackend_Create(name ?? ""));
            await commander.Call(new UsersBackend_LinkSession(session.Id, userId));
            return Results.Ok(new { userId });
        });
        app.MapPost("/dev/signout", async (ISessionResolver sessionResolver, ICommander commander) => {
            var session = await sessionResolver.GetSession();
            await commander.Call(new UsersBackend_UnlinkSession(session.Id));
            return Results.Ok(new { ok = true });
        });
    }
}

using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration.Memory;
using TownHall;
using TownHall.Host;
using TownHall.Host.Components.Pages;
using TownHall.Db;
using TownHall.Host.Services;
using TownHall.UI;
using TownHall.UI.Services;

var builder = WebApplication.CreateBuilder(args);
var env = builder.Environment;
var cfg = builder.Configuration;
var hostSettings = cfg.GetSection("Host").Get<HostSettings>() ?? new HostSettings();

// Appended (highest priority) so the app always binds this port - even under the Aspire AppHost,
// which would otherwise inject its own ASPNETCORE_URLS. Keeps the server-loop probe + URLs on :5136.
cfg.Sources.Add(new MemoryConfigurationSource() {
    InitialData = new Dictionary<string, string>(StringComparer.Ordinal) {
        { WebHostDefaults.ServerUrlsKey, $"http://localhost:{hostSettings.Port ?? 5136}" },
    }!
});

// Configure services
var services = builder.Services;
ConfigureLogging();
// After ConfigureLogging (which ClearProviders + AddConsole), so the OTel logging provider survives.
// OpenTelemetry (logs/traces/metrics) streams to the Aspire dashboard when run under the AppHost.
builder.AddServiceDefaults();
ConfigureServices();
builder.WebHost.UseDefaultServiceProvider((ctx, options) => {
    // ValidateOnBuild stays off: the server-direct services are factory-registered and read the
    // per-circuit CircuitSession, which isn't set until App initializes a circuit.
    if (ctx.HostingEnvironment.IsDevelopment())
        options.ValidateScopes = true;
});

// Build & configure app
var app = builder.Build();
ConfigureApp();

// Migrate the DB to the latest schema (see src/TownHall.Db/Migrations). The schema (incl. the unused
// Fusion operation tables) is kept identical across framework branches so they share one DB.
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

    // EF Core (plain - no Fusion operations layer)
    services.AddDbContextFactory<AppDbContext>(db => {
        db.UseNpgsql(hostSettings.PostgreSql);
        db.AddInterceptors(new DbMetrics()); // Query + fetched-row counters (TownHall.Db meter)
        // The migration/snapshot intentionally still model the (unused) Fusion operation tables, so the
        // schema stays identical across framework branches; our lean model doesn't, so ignore the mismatch.
        db.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        if (env.IsDevelopment())
            db.EnableSensitiveDataLogging();
    });

    // Reactive core (shared singletons)
    services.AddSingleton<ChangeTracker>();
    services.AddSingleton<PresenceStore>();

    // Backend services - local (not hub-exposed); they do the work on ids and assume the caller
    // is already authorized.
    services.AddSingleton<IUsersBackend, UsersBackend>();
    services.AddSingleton<IRoomsBackend, RoomsBackend>();
    services.AddSingleton<IQuestionsBackend, QuestionsBackend>();
    services.AddSingleton<IRoomStatsBackend, RoomStatsBackend>();
    services.AddSingleton<IPresenceBackend, PresenceBackend>();
    services.AddSingleton<IMoodBackend, MoodBackend>();
    services.AddSingleton<ServerShared>();

    // Passkey (WebAuthn) infrastructure
    services.AddSingleton<PasskeyChallengeStore>();
    services.AddSingleton(new Fido2NetLib.Fido2Configuration {
        ServerDomain = hostSettings.PasskeyRpId,
        ServerName = "TownHall",
        Origins = hostSettings.PasskeyOrigins.ToHashSet(StringComparer.Ordinal),
    });

    // SignalR (the app's data hub, used by the WASM client) - MessagePack wire protocol
    services.AddSignalR(o => {
        o.EnableDetailedErrors = env.IsDevelopment();
        o.AddFilter<ErrorHubFilter>();
    }).AddMessagePackProtocol(HubProtocolConfig.Configure);

    // Web
    services.AddRazorComponents()
        .AddInteractiveServerComponents()
        .AddInteractiveWebAssemblyComponents();

    // Shared services
    ClientStartup.ConfigureSharedServices(services);

    // Server-direct implementations of the reactive interfaces (used in Server/Auto render modes),
    // each bound to the browser's session for the current circuit. In WASM the same interfaces are
    // provided by the hub-routing clients in the separate WASM container.
    services.AddScoped<CircuitSession>();
    services.AddScoped<IUsers>(sp => new UsersService(sp.GetRequiredService<ServerShared>(), CircuitIdentity(sp)));
    services.AddScoped<IAuth>(sp => new AuthService(sp.GetRequiredService<ServerShared>(), CircuitIdentity(sp)));
    services.AddScoped<IRooms>(sp => new RoomsService(sp.GetRequiredService<ServerShared>(), CircuitIdentity(sp)));
    services.AddScoped<IQuestions>(sp => new QuestionsService(sp.GetRequiredService<ServerShared>(), CircuitIdentity(sp)));
    services.AddScoped<IRoomStats>(sp => new RoomStatsService(sp.GetRequiredService<ServerShared>(), CircuitIdentity(sp)));
    services.AddScoped<IPresence>(sp => new PresenceService(sp.GetRequiredService<ServerShared>(), CircuitIdentity(sp)));
    services.AddScoped<IMood>(sp => new MoodService(sp.GetRequiredService<ServerShared>(), CircuitIdentity(sp)));
    return;

    static Identity CircuitIdentity(IServiceProvider sp)
        => Identity.Of(sp.GetRequiredService<CircuitSession>().SessionId);
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

    // Ensure a per-browser session cookie exists before the WASM client opens its hub connection, and
    // expose the resolved id via HttpContext.Items so the server-render pass (_HostPage) uses the SAME
    // id as the cookie even on the first request (when the cookie is only in the response, not the
    // request) - otherwise the server-render session and the hub's cookie session would diverge.
    app.Use(async (context, next) => {
        var sessionId = context.Request.Cookies[TownHallHub.SessionCookieName];
        if (sessionId is not { Length: >= 8 }) {
            sessionId = Guid.NewGuid().ToString("N");
            context.Response.Cookies.Append(TownHallHub.SessionCookieName, sessionId,
                new CookieOptions {
                    HttpOnly = true,
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    MaxAge = TimeSpan.FromDays(365),
                });
        }
        context.Items[TownHallHub.SessionCookieName] = sessionId;
        await next();
    });

    app.UseAntiforgery();

    // Razor components
    app.MapStaticAssets();
    app.MapRazorComponents<_HostPage>()
        .AddInteractiveServerRenderMode()
        .AddInteractiveWebAssemblyRenderMode()
        .AddAdditionalAssemblies(typeof(App).Assembly);

    // SignalR endpoints
    app.MapHub<TownHallHub>("/townhall-hub");

    // Dev-only sign-in without a passkey - for automated/manual testing of the signed-in flows.
    // Gated to Development; never mapped in production.
    if (env.IsDevelopment()) {
        app.MapPost("/dev/signin", async (HttpContext ctx, IUsersBackend users, string? name) => {
            var sessionId = ctx.Request.Cookies[TownHallHub.SessionCookieName] ?? "";
            if (sessionId.Length < 8)
                return Results.BadRequest();

            var userId = await users.Create(new UsersBackend_Create(name ?? ""));
            await users.LinkSession(new UsersBackend_LinkSession(sessionId, userId));
            return Results.Ok(new { userId });
        });
        app.MapPost("/dev/signout", async (HttpContext ctx, IUsersBackend users) => {
            var sessionId = ctx.Request.Cookies[TownHallHub.SessionCookieName] ?? "";
            await users.UnlinkSession(new UsersBackend_UnlinkSession(sessionId));
            return Results.Ok(new { ok = true });
        });
    }

    // Persist the chosen Blazor render mode (Auto/Server/WASM) in a cookie, then reload into it
    app.MapGet("/render-mode/{key}", (HttpContext context, string key, string? redirectTo) => {
        var mode = RenderModeDef.GetOrDefault(key);
        context.Response.Cookies.Append("RenderMode", mode.Key, new CookieOptions {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(365),
        });
        return Results.Redirect(string.IsNullOrEmpty(redirectTo) ? "/" : redirectTo);
    });
}

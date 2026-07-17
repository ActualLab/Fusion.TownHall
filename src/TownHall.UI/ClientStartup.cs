using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using TownHall.UI.Services;

namespace TownHall.UI;

public static class ClientStartup
{
    // WASM container: the SignalR connection + client-side (hub-routing) implementations of the interfaces.
    public static void ConfigureServices(IServiceCollection services, WebAssemblyHostBuilder builder)
    {
        var logging = builder.Logging;
        logging.SetMinimumLevel(LogLevel.Warning);
        logging.AddFilter(typeof(App).Namespace, LogLevel.Debug);

        services.AddScoped<TownHallClient>();
        services.AddScoped<IUsers, UsersClient>();
        services.AddScoped<IAuth, AuthClient>();
        services.AddScoped<IRooms, RoomsClient>();
        services.AddScoped<IQuestions, QuestionsClient>();
        services.AddScoped<IRoomStats, RoomStatsClient>();
        services.AddScoped<IPresence, PresenceClient>();
        services.AddScoped<IMood, MoodClient>();

        ConfigureSharedServices(services);
    }

    // Shared services configured on both the client and the server.
    public static void ConfigureSharedServices(IServiceCollection services)
    {
        services.AddScoped<TownHall.UI.Services.RenderModeState>();
        services.AddScoped<TownHall.UI.Services.UiCommander>();
        services.AddScoped<TownHall.UI.Services.LayoutState>();
        services.AddScoped<TownHall.UI.Services.PasskeyClient>();
        services.AddMudServices();
    }
}

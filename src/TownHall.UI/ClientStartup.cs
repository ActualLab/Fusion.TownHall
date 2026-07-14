using ActualLab.Fusion.Blazor;
using ActualLab.Fusion.UI;
using ActualLab.Rpc;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

namespace TownHall.UI;

public static class ClientStartup
{
    public static void ConfigureServices(IServiceCollection services, WebAssemblyHostBuilder builder)
    {
        var logging = builder.Logging;
        logging.SetMinimumLevel(LogLevel.Warning);
        logging.AddFilter(typeof(App).Namespace, LogLevel.Debug);

        // Fusion services
        var fusion = services.AddFusion();
        fusion.Rpc.AddWebSocketClient(builder.HostEnvironment.BaseAddress);

        // RPC clients
        fusion.AddClient<IParticipants>();
        fusion.AddClient<IRooms>();
        fusion.AddClient<IQuestions>();
        fusion.AddClient<IRoomStats>();
        fusion.AddClient<IPresence>();
        fusion.AddClient<IMood>();

        ConfigureSharedServices(services);
    }

    // Shared services configured on both the client and the server.
    // On the server, fusion.AddBlazor() must follow services.AddServerSideBlazor().
    public static void ConfigureSharedServices(IServiceCollection services)
    {
        // The contracts carry MessagePack attributes only, so RPC must not default to MemoryPack
        RpcSerializationFormatResolver.Default = new("msgpack6c");

        var fusion = services.AddFusion();
        fusion.AddBlazor();
        services.AddScoped<IUpdateDelayer>(c => new UpdateDelayer(c.UIActionTracker(), 0.25)); // 0.25s
        services.AddMudServices();
    }
}

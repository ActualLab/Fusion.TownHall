using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Aspire-style "service defaults": wires OpenTelemetry so the app streams logs, metrics and traces to
// the Aspire dashboard (via OTLP) when run under the AppHost. The tracing sources are chosen so a
// single trace shows a Fusion RPC call / command together with the exact DB commands it triggered.
public static class ServiceDefaultsExtensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging => {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                // Fusion stack metrics: RPC call error/duration, CommandR command execution, Fusion
                // compute registry (cached/pruned computed counts)
                .AddMeter("ActualLab.Rpc")
                .AddMeter("ActualLab.CommandR")
                .AddMeter("ActualLab.Fusion")
                // PostgreSQL provider metrics: connection pool usage, bytes read/written, operation
                // duration, prepared-statement ratio
                .AddMeter("Npgsql")
                // App DB counters: query count/rate + fetched-row count/rate (from the EF interceptor)
                .AddMeter("TownHall.Db"))
            .WithTracing(tracing => tracing
                // Capture every trace (dev) so nothing is head-sampled away in the dashboard
                .SetSampler(new AlwaysOnSampler())
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                // Fusion's own spans (RPC inbound/outbound calls, command execution, EF entity resolver)
                // and the actual DB commands (AddNpgsql) - together a trace shows an RPC call and the DB
                // hits it triggered
                .AddSource("ActualLab.Rpc")
                .AddSource("ActualLab.CommandR")
                .AddSource("ActualLab.Fusion")
                .AddNpgsql());

        // Under the Aspire AppHost this env var points at the dashboard's OTLP receiver
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            builder.Services.AddOpenTelemetry().UseOtlpExporter();

        return builder;
    }
}

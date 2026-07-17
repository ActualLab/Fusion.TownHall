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
// single trace shows a hub call / stream read together with the exact DB commands it triggered.
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
                // Custom stream-read spans, SignalR's own hub spans, and the actual DB commands
                // (AddNpgsql) - together a trace shows a hub call / stream read and the DB hits it
                // triggered
                .AddSource("TownHall")
                .AddSource("Microsoft.AspNetCore.SignalR.Server")
                .AddNpgsql());

        // Under the Aspire AppHost this env var points at the dashboard's OTLP receiver
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            builder.Services.AddOpenTelemetry().UseOtlpExporter();

        return builder;
    }
}

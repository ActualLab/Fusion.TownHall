using Aspire.Hosting;

// Runs the TownHall host under the Aspire dashboard with full observability (logs + traces + metrics via
// OTLP). The host keeps its own fixed port (5136) so the server-loop probe and browser URLs are unchanged;
// we don't declare an Aspire endpoint, so Aspire just supervises the process and collects its telemetry.
var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.TownHall_Host>("townhall", options => options.ExcludeLaunchProfile = true)
    .WithEnvironment("Host__Port", "5136")
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development");

builder.Build().Run();

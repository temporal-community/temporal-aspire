using TemporalCommunity.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var temporal = builder.AddTemporalDevContainer("temporal");

var dependent = builder.AddExecutable(
    "dependent",
    OperatingSystem.IsWindows() ? "pwsh" : "/bin/sh",
    builder.AppHostDirectory);

if (OperatingSystem.IsWindows())
    dependent.WithArgs("-NoProfile", "-Command", "Start-Sleep -Seconds 600");
else
    dependent.WithArgs("-c", "sleep 600");

dependent
    .WaitFor(temporal)
    .WithReference(temporal);

builder.Build().Run();

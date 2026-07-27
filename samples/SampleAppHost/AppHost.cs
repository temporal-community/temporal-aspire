using TemporalCommunity.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var temporalDataDirectory = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".temporal"));
Directory.CreateDirectory(temporalDataDirectory);

var temporal = builder.AddTemporalLocalDevServer("temporal", options =>
{
    options.DevServerOptions.DatabaseFilename = Path.Combine(temporalDataDirectory, "temporal.db");
});

builder.AddProject<Projects.SampleWorker>("sample-worker")
    .WaitFor(temporal)
    .WithReference(temporal);

builder.AddProject<Projects.SampleClient>("sample-client")
    .WaitFor(temporal)
    .WithReference(temporal);

builder.Build().Run();

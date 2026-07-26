using TemporalCommunity.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var temporalDataDirectory = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".temporal"));
Directory.CreateDirectory(temporalDataDirectory);

var temporal = builder.AddTemporalLocalDevServer("temporal", options =>
{
    options.DevServerOptions.DatabaseFilename = Path.Combine(temporalDataDirectory, "temporal.db");
});

builder.AddProject<Projects.TemporalCommunity_Aspire_Hosting_SampleWorker>("sample-worker")
    .WaitFor(temporal)
    .WithReference(temporal);

builder.AddProject<Projects.TemporalCommunity_Aspire_Hosting_SampleClient>("sample-client")
    .WaitFor(temporal)
    .WithReference(temporal);

builder.Build().Run();

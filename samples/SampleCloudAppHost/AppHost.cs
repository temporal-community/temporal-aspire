using TemporalCommunity.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var temporalAddress = builder.AddParameter("temporal-address");
var temporalNamespace = builder.AddParameter("temporal-namespace");
var temporalApiKey = builder.AddParameter("temporal-api-key", secret: true);
var temporalUiAddress = builder.AddParameter("temporal-ui-address");

var temporal = builder.AddTemporalCloud(
    "temporal",
    temporalAddress,
    temporalNamespace,
    temporalApiKey,
    temporalUiAddress,
    configure: options => options.EnableHealthCheck = true);

builder.AddProject<Projects.SampleWorker>("sample-worker")
    .WaitFor(temporal)
    .WithReference(temporal);

builder.AddProject<Projects.SampleClient>("sample-client")
    .WaitFor(temporal)
    .WithReference(temporal);

builder.Build().Run();

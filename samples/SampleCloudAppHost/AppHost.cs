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
    configure: options => options.EnableHealthCheck = true)
    .WithCliTerminal();

builder.AddDotnetProject("sample-worker", "../SampleWorker/SampleWorker.csproj")
    .WaitFor(temporal)
    .WithReference(temporal);

builder.AddDotnetProject("sample-client", "../SampleClient/SampleClient.csproj")
    .WaitFor(temporal)
    .WithReference(temporal);

builder.Build().Run();

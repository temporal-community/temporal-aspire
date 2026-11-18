using TemporalCommunity.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var temporal = builder.AddTemporalCliServer("temporal")
    .WithDataVolume("temporal-data")
    .WithCliTerminal();

builder.AddDotnetProject("sample-worker", "../SampleWorker/SampleWorker.csproj")
    .WaitFor(temporal)
    .WithReference(temporal);

builder.AddDotnetProject("sample-client", "../SampleClient/SampleClient.csproj")
    .WaitFor(temporal)
    .WithReference(temporal);

builder.Build().Run();

# Temporal Community Aspire Hosting

`TemporalCommunity.Aspire.Hosting` is a .NET Aspire hosting integration for running Temporal development server resources from an Aspire AppHost.

## Features

- **Local development server** using `Temporalio.Testing.WorkflowEnvironment`
- **Container-based development server** using the `temporalio/temporal` Docker image
- **CLI-based development server** using the Temporal CLI
- **Service discovery** through Aspire resource references
- **Environment variable injection** for dependent worker/client projects
- **Health checks** integrated with Aspire resource health

## Install

```bash
dotnet add package TemporalCommunity.Aspire.Hosting
```

## Usage

Add a Temporal resource to your AppHost and reference it from projects that need Temporal connectivity.

```csharp
using TemporalCommunity.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var temporal = builder.AddTemporalLocalDevServer("temporal");

builder.AddProject<Projects.Worker>("worker")
    .WaitFor(temporal)
    .WithReference(temporal);

builder.Build().Run();
```

`WithReference` injects:

- `TEMPORAL_ADDRESS`
- `TEMPORAL_UI_ADDRESS`
- `TEMPORAL_NAMESPACE`

## Other resource modes

```csharp
var temporal = builder.AddTemporalDevContainer();
```

```csharp
var temporal = builder.AddTemporalCliServer();
```

`AddTemporalDevContainer` requires Docker. `AddTemporalCliServer` requires the Temporal CLI on `PATH`.

## Persist local development state

The local development server uses an in-memory database by default. To persist state between runs, set Temporal's dev-server database filename:

```csharp
var temporal = builder.AddTemporalLocalDevServer("temporal", options =>
{
    options.DevServerOptions.DatabaseFilename = "temporal.db";
});
```

For the container-based resource, use a container path and mount a volume:

```csharp
var temporal = builder.AddTemporalDevContainer("temporal", options =>
{
    options.DevServerOptions.DatabaseFilename = "/home/temporal/temporal.db";
});

temporal.WithVolume("temporal-data", "/home/temporal");
```

`DevServerOptions` is marked unstable by the Temporal .NET SDK and may change in future SDK versions.

## Run the sample

This repo includes a runnable Aspire sample with an AppHost, worker, client, and workflow.

```bash
aspire start --apphost samples/TemporalCommunity.Aspire.Hosting.SampleAppHost/TemporalCommunity.Aspire.Hosting.SampleAppHost.csproj
```

The sample starts a local Temporal development server, runs a worker, and executes a workflow from the client.

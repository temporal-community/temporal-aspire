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

## Temporal Cloud

Use `AddTemporalCloud` for an externally managed Temporal Cloud namespace. The resource injects connection settings into referenced projects without adding Aspire service discovery for the external endpoint.

```csharp
var temporalAddress = builder.AddParameter("temporal-address");
var temporalNamespace = builder.AddParameter("temporal-namespace");
var temporalApiKey = builder.AddParameter("temporal-api-key", secret: true);
var temporalUiAddress = builder.AddParameter("temporal-ui-address");

var temporal = builder.AddTemporalCloud(
    "temporal",
    temporalAddress,
    temporalNamespace,
    temporalApiKey,
    temporalUiAddress);

builder.AddProject<Projects.Worker>("worker")
    .WithReference(temporal);
```

Use Aspire parameters for production configuration, especially `builder.AddParameter("temporal-api-key", secret: true)` for API keys. The string overload is intended for non-secret local or test configuration; do not put production API keys in source.

`WithReference` injects `TEMPORAL_ADDRESS`, `TEMPORAL_NAMESPACE`, `TEMPORAL_API_KEY`, and optional `TEMPORAL_UI_ADDRESS` when configured. Consumers should load those values with the Temporal .NET SDK environment config (`ClientEnvConfig.LoadClientConnectOptions()`); the SDK maps `TEMPORAL_API_KEY` to `TemporalClientConnectOptions.ApiKey` and enables TLS automatically.

## Production deployment guidance

Use `AddTemporalCloud` or another externally managed Temporal endpoint for production deployments. The local, CLI, and container resources are intended for development and are excluded from generated Aspire deployment manifests.

Temporal workers are external processes that poll task queues. Queued Workflow and Activity tasks remain in Temporal when no workers are available, but worker scaling changes user-visible latency:

- Keep at least one worker replica running for latency-sensitive task queues and workflows with frequent activity.
- Use KEDA or Temporal Worker Controller scale-to-zero patterns for long-idle workloads when cold-start latency is acceptable.
- Size workers by task queue and workload characteristics rather than applying one replica policy globally.

For Azure Container Apps or Kubernetes, inject Temporal Cloud address, namespace, and API key as parameters or secrets, then let worker/client code load them with `ClientEnvConfig.LoadClientConnectOptions()`.

## Persist local development state

The local development server uses an in-memory database by default. To persist state between runs, set Temporal's dev-server database filename:

```csharp
var temporalDataDirectory = Path.Combine(AppContext.BaseDirectory, "temporal-data");
Directory.CreateDirectory(temporalDataDirectory);

var temporal = builder.AddTemporalLocalDevServer("temporal", options =>
{
    options.DevServerOptions.DatabaseFilename = Path.Combine(temporalDataDirectory, "temporal.db");
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

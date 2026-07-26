# TemporalCommunity.Aspire.Hosting

`TemporalCommunity.Aspire.Hosting` is a .NET Aspire hosting integration for running Temporal development server resources from an AppHost.

## Install

```bash
dotnet add package TemporalCommunity.Aspire.Hosting
```

## Usage

Add a Temporal resource to your AppHost and reference it from projects that need Temporal connection details.

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var temporal = builder.AddTemporalLocalDevServer();

builder.AddProject<Projects.Worker>("worker")
    .WithReference(temporal);

builder.Build().Run();
```

`WithReference` injects `TEMPORAL_ADDRESS`, `TEMPORAL_UI_ADDRESS`, and `TEMPORAL_NAMESPACE` into the referenced project. Codec settings are also injected when configured.

The package also includes `AddTemporalDevContainer` for Docker-based local development and `AddTemporalCliServer` for running the Temporal CLI dev server directly.

## Temporal Cloud

Use `AddTemporalCloud` for an externally managed Temporal Cloud namespace:

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

## Production guidance

Use `AddTemporalCloud` or another externally managed Temporal endpoint for production deployments. The local, CLI, and container resources are intended for development and are excluded from generated Aspire deployment manifests.

Temporal workers poll task queues from outside Temporal. Queued work remains durable if no workers are running, but latency depends on how quickly workers are available. Keep workers always-on for latency-sensitive task queues; use KEDA or Temporal Worker Controller scale-to-zero patterns for long-idle workloads when cold-start latency is acceptable.

## Persist development state

Set Temporal's dev-server database filename to persist local development state:

```csharp
var temporalDataDirectory = Path.Combine(AppContext.BaseDirectory, "temporal-data");
Directory.CreateDirectory(temporalDataDirectory);

var temporal = builder.AddTemporalLocalDevServer("temporal", options =>
{
    options.DevServerOptions.DatabaseFilename = Path.Combine(temporalDataDirectory, "temporal.db");
});
```

For `AddTemporalDevContainer`, use a container path and mount a volume:

```csharp
var temporal = builder.AddTemporalDevContainer("temporal", options =>
{
    options.DevServerOptions.DatabaseFilename = "/home/temporal/temporal.db";
});

temporal.WithVolume("temporal-data", "/home/temporal");
```

`DevServerOptions` is marked unstable by the Temporal .NET SDK and may change in future SDK versions.

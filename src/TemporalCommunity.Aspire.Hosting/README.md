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
var temporalCodecAuth = builder.AddParameter("temporal-codec-auth", secret: true);

var temporal = builder.AddTemporalCloud(
    "temporal",
    temporalAddress,
    temporalNamespace,
    temporalApiKey,
    temporalUiAddress,
    temporalCodecAuth,
    configure: options => options.EnableHealthCheck = true);

builder.AddProject<Projects.Worker>("worker")
    .WithReference(temporal);
```

Use Aspire parameters for production configuration, especially `builder.AddParameter("temporal-api-key", secret: true)` for API keys. The string overload only accepts address and namespace; API keys and UI addresses require Aspire parameters or the configure overload.

When a UI address is configured, Aspire shows the resource source as **Temporal Cloud** and adds a **Temporal Dashboard** link to the resource row.

`WithReference` injects `TEMPORAL_ADDRESS`, `TEMPORAL_NAMESPACE`, `TEMPORAL_API_KEY`, and optional `TEMPORAL_UI_ADDRESS` and `TEMPORAL_CODEC_AUTH` when configured. Use secret Aspire parameters for API keys and codec credentials. Consumers should load connection values with the Temporal .NET SDK environment config (`ClientEnvConfig.LoadClientConnectOptions()`); the SDK maps `TEMPORAL_API_KEY` to `TemporalClientConnectOptions.ApiKey` and enables TLS automatically.

Set `EnableHealthCheck` only when you want the AppHost to make an authenticated `GetSystemInfo` call to Temporal Cloud. Aspire displays the result on the Cloud resource and uses it for `WaitFor`, but this verifies endpoint reachability, TLS, and credentials only. It does not verify worker polling, task-queue capacity, or Temporal Cloud-wide availability. The Cloud resource remains excluded from deployment manifests, so this does not create an Azure Container Apps or Kubernetes readiness probe.

## Production guidance

Use `AddTemporalCloud` or another externally managed Temporal endpoint for production deployments. The local, CLI, and container resources are intended for development and are excluded from generated Aspire deployment manifests.

Temporal workers poll task queues from outside Temporal. Queued work remains durable if no workers are running, but latency depends on how quickly workers are available. Run at least two worker replicas for every production task queue and keep them always-on for latency-sensitive work; use KEDA or Temporal Worker Controller scale-to-zero patterns only when the workload can tolerate cold-start latency. Tune task slots, sticky-cache size, and poller counts from load tests, use Worker Versioning for workflow-code rollouts, configure graceful shutdown, and monitor worker CPU/memory, Schedule-to-Start latency, available task slots, and Temporal request failures/latency together.

## Persist development state

Set Temporal's dev-server database filename to persist local development state:

```csharp
var appHostDirectory = FindAncestor(AppContext.BaseDirectory, "MyApp.AppHost");
var temporalDataDirectory = Path.Combine(Directory.GetParent(appHostDirectory)!.FullName, ".temporal");
Directory.CreateDirectory(temporalDataDirectory);

var temporal = builder.AddTemporalLocalDevServer("temporal", options =>
{
    options.DevServerOptions.DatabaseFilename = Path.Combine(temporalDataDirectory, "temporal.db");
});

static string FindAncestor(string startPath, string directoryName)
{
    var directory = new DirectoryInfo(startPath);
    while (directory is not null)
    {
        if (string.Equals(directory.Name, directoryName, StringComparison.Ordinal))
            return directory.FullName;

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException($"Could not find ancestor directory '{directoryName}'.");
}
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

When `UI = false`, the resource does not publish a dashboard URL or `TEMPORAL_UI_ADDRESS`. Development resources expose `/metrics` on the configured metrics port.

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

Set the parameter values for local development with the Aspire CLI. The key format is `Parameters:<parameter-name>`, and `--apphost` targets the AppHost project that declares the parameters:

```bash
aspire secret set Parameters:temporal-address "your-namespace.your-account.tmprl.cloud:7233" \
  --apphost samples/SampleAppHost/SampleAppHost.csproj

aspire secret set Parameters:temporal-namespace "your-namespace.your-account" \
  --apphost samples/SampleAppHost/SampleAppHost.csproj

aspire secret set Parameters:temporal-api-key "your-api-key" \
  --apphost samples/SampleAppHost/SampleAppHost.csproj

aspire secret set Parameters:temporal-ui-address "https://cloud.temporal.io/namespaces/your-namespace.your-account" \
  --apphost samples/SampleAppHost/SampleAppHost.csproj

aspire secret set Parameters:temporal-codec-auth "your-codec-auth-token" \
  --apphost samples/SampleAppHost/SampleAppHost.csproj
```

Use `aspire secret list --apphost <path-to-apphost>` to confirm the values were saved. Keep API keys and codec credentials in user secrets or your deployment secret store; do not commit them to source control.

`WithReference` injects `TEMPORAL_ADDRESS`, `TEMPORAL_NAMESPACE`, `TEMPORAL_API_KEY`, and optional `TEMPORAL_UI_ADDRESS` and `TEMPORAL_CODEC_AUTH` when configured. Use secret Aspire parameters for API keys and codec credentials. Consumers should load connection values with the Temporal .NET SDK environment config (`ClientEnvConfig.LoadClientConnectOptions()`); the SDK maps `TEMPORAL_API_KEY` to `TemporalClientConnectOptions.ApiKey` and enables TLS automatically.

Set `EnableHealthCheck` only when you want the AppHost to make an authenticated `GetSystemInfo` call to Temporal Cloud. Aspire displays the result on the Cloud resource and uses it for `WaitFor`, but this verifies endpoint reachability, TLS, and credentials only. It does not verify worker polling, task-queue capacity, or Temporal Cloud-wide availability. The Cloud resource remains excluded from deployment manifests, so this does not create an Azure Container Apps or Kubernetes readiness probe.

## Production deployment guidance

Use `AddTemporalCloud` or another externally managed Temporal endpoint for production deployments. The local, CLI, and container resources are intended for development and are excluded from generated Aspire deployment manifests.

Temporal workers are external processes that poll task queues. Queued Workflow and Activity tasks remain in Temporal when no workers are available, but worker scaling changes user-visible latency:

- Run at least two worker replicas for each production task queue so a rollout or instance failure does not stop polling; keep them always-on for latency-sensitive work.
- Use KEDA or Temporal Worker Controller scale-to-zero patterns for long-idle workloads when cold-start latency is acceptable.
- Size workers by task queue and workload characteristics rather than applying one replica policy globally.
- Tune task slots, sticky-cache size, and poller counts from representative load tests; do not assume SDK defaults are production settings.
- Use Worker Versioning for workflow-code rollouts, configure graceful shutdown so active Tasks can finish, and monitor worker CPU/memory, Schedule-to-Start latency, available task slots, and Temporal request failures/latency together.

For Azure Container Apps or Kubernetes, inject Temporal Cloud address, namespace, and API key as parameters or secrets, then let worker/client code load them with `ClientEnvConfig.LoadClientConnectOptions()`.

## Persist local development state

The local development server uses an in-memory database by default. To persist state between runs, set Temporal's dev-server database filename:

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

For the container-based resource, use a container path and mount a volume:

```csharp
var temporal = builder.AddTemporalDevContainer("temporal", options =>
{
    options.DevServerOptions.DatabaseFilename = "/home/temporal/temporal.db";
});

temporal.WithVolume("temporal-data", "/home/temporal");
```

`DevServerOptions` is marked unstable by the Temporal .NET SDK and may change in future SDK versions.

When `UI = false`, the resource does not publish a dashboard URL or `TEMPORAL_UI_ADDRESS`. Development resources expose `/metrics` on the configured metrics port.

## Run the sample

This repo includes a runnable Aspire sample with an AppHost, worker, client, and workflow.

```bash
aspire start --apphost samples/SampleAppHost/SampleAppHost.csproj
```

The sample starts a local Temporal development server, runs a worker, and executes a workflow from the client.

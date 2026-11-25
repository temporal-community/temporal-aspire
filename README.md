# Temporal Community Aspire Hosting

`TemporalCommunity.Aspire.Hosting` is a .NET Aspire hosting integration for running Temporal development server resources from an Aspire AppHost.

## Features

- **Local development server** using `Temporalio.Testing.WorkflowEnvironment`
- **Container-based development server** using the `temporalio/temporal` image
- **CLI-based development server** using the Temporal CLI
- **Service discovery** through Aspire resource references
- **Environment variable injection** for dependent worker/client projects
- **Health checks** integrated with Aspire resource health

## Install

The package supports these AppHost target frameworks:

| AppHost target framework | .NET SDK |
| --- | --- |
| `net10.0` | .NET 10 SDK |
| `net11.0` | .NET 11 SDK |

Both targets require .NET Aspire 17.0 or later. Building this repository requires the .NET 11 SDK because its default target framework is `net11.0`. The examples using `AddDotnetProject` also require the `Aspire.Hosting.Dotnet` package in the AppHost.

```bash
dotnet add package TemporalCommunity.Aspire.Hosting
```

## Usage

Add a Temporal resource to your AppHost and reference it from projects that need Temporal connectivity.

```csharp
using TemporalCommunity.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var temporal = builder.AddTemporalLocalDevServer("temporal");

builder.AddDotnetProject("worker", "../Worker/Worker.csproj")
    .WaitFor(temporal)
    .WithReference(temporal);

builder.Build().Run();
```

`WithReference` injects:

- `TEMPORAL_ADDRESS`
- `TEMPORAL_UI_ADDRESS` (when a UI address is configured)
- `TEMPORAL_NAMESPACE`

A worker can load those settings into Temporal SDK client options and pass them to its hosted worker:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Temporalio.Common.EnvConfig;
using Temporalio.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
var connectOptions = ClientEnvConfig.LoadClientConnectOptions();

builder.Services.AddHostedTemporalWorker(taskQueue: "my-task-queue")
    .ConfigureOptions(options => options.ClientOptions = connectOptions)
    .AddWorkflow<MyWorkflow>();

await builder.Build().RunAsync();
```

`MyWorkflow` is the workflow implementation registered by your worker.

## Other resource modes

```csharp
var temporal = builder.AddTemporalDevContainer();
```

```csharp
var temporal = builder.AddTemporalCliServer();
```

`AddTemporalDevContainer` requires an Aspire-supported container runtime, such as Docker or Podman. `AddTemporalCliServer` requires the Temporal CLI on `PATH`.

By default, Aspire allocates free host ports for the container while Temporal continues to listen inside the container on 7233 (gRPC), 8233 (UI), and 9233 (metrics). `WithReference` resolves the allocated ports automatically, so multiple AppHosts can run side by side without sharing a Temporal server accidentally.

To request fixed host ports, configure non-default values explicitly:

```csharp
var temporal = builder.AddTemporalDevContainer("temporal", options =>
{
    options.TargetHost = "0.0.0.0:7399";
    options.UIPort = 8399;
    options.MetricsPort = 9399;
});
```

## Dashboard terminal

Add `.WithCliTerminal()` to a Temporal resource to show **Temporal Terminal** in the Aspire Dashboard during local AppHost runs:

```csharp
var temporal = builder.AddTemporalDevContainer("temporal")
    .WithCliTerminal();
```

The same extension works with `AddTemporalCliServer`, `AddTemporalLocalDevServer`, and `AddTemporalCloud`. It opens an interactive shell with `TEMPORAL_ADDRESS` and `TEMPORAL_NAMESPACE` configured for the running server. Run commands such as `temporal workflow list` directly in that shell. The command is enabled only while the resource is running and healthy. Cloud resources must enable the health check (`EnableHealthCheck = true`) for the terminal command to become available.

Container terminals use the CLI bundled in the image through Aspire's selected Docker or Podman runtime; no host Temporal CLI installation is needed. CLI, SDK-managed, and Cloud terminals use a host shell and require the [Temporal CLI](https://docs.temporal.io/cli/setup-cli) on `PATH`. Cloud terminals use the resource's API key, address, and namespace with TLS enabled. Anyone who can open the Dashboard terminal can run authenticated CLI commands against that namespace, so enable it only for trusted Dashboard users. The action is omitted when publishing or deploying the AppHost.

The Aspire CLI bundle supplies Dashboard terminal support and is enabled by default for C# AppHosts in Aspire 17.

## Temporal Cloud

Use `AddTemporalCloud` to reference an externally managed Temporal Cloud namespace.

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
    temporalUiAddress,
    configure: options => options.EnableHealthCheck = true);

builder.AddDotnetProject("worker", "../Worker/Worker.csproj")
    .WithReference(temporal);
```

Use secret parameters for API keys and codec credentials. A UI address adds a **Temporal Dashboard** link in Aspire. `WithReference` supplies the Temporal connection environment variables to consuming projects, which can load them with `ClientEnvConfig.LoadClientConnectOptions()`.

`EnableHealthCheck` is `false` by default, so the AppHost does not make remote health-check calls unless you opt in. Set it to `true` when you want Aspire to verify the endpoint, TLS, and credentials with `GetSystemInfo`, gate dependent resources using `.WaitFor(temporal)`, or enable the Cloud **Temporal Terminal** action. It does not verify worker or task-queue health.

## Persist local development state

For CLI and container servers, use `.WithDataVolume()` to let Aspire generate a storage name, or pass a name to keep control of the storage identity:

```csharp
var temporal = builder.AddTemporalCliServer("temporal")
    .WithDataVolume();
```

The same extension works with `AddTemporalDevContainer`; pass a name such as `.WithDataVolume("temporal-data")` when you need an explicit container volume name.

Use `AddTemporalDevContainer` instead for a container server. Both follow [Aspire's portable volume path conventions](https://aspire.dev/fundamentals/persist-data-volumes/#use-a-portable-volume-path-environment-variable), so you do not need to configure a host database path:

- **CLI:** Aspire creates a resource-scoped directory in its local store.
- **Container:** Aspire mounts a named volume at `/home/temporal`.

Temporal writes `temporal.db` into the resolved directory. Keep the AppHost location, resource name, and storage name stable to reuse CLI data. The CLI store is normally under the AppHost's `obj/.aspire` directory; removing `obj` removes that data. CLI directories and container volumes are separate storage, so switching modes does not migrate an existing database.

For these modes, choose either `.WithDataVolume()` or `DevServerOptions.DatabaseFilename`; configuring both is rejected. Existing explicit filenames and container mounts continue to work.

The SDK-managed server (`AddTemporalLocalDevServer`) does not support `.WithDataVolume()`. It uses an in-memory database by default; persistence requires an explicit `DevServerOptions.DatabaseFilename` and an existing parent directory.

The SDK's [`DevServerOptions`](https://github.com/temporalio/sdk-dotnet/blob/main/src/Temporalio/Testing/DevServerOptions.cs) are specific to the Temporal CLI development server implementation and may no longer be valid if that implementation changes.

When `UI = false`, the resource does not publish a dashboard URL or `TEMPORAL_UI_ADDRESS`. Development resources expose `/metrics`; container host ports are allocated unless non-default ports are configured explicitly.

## Run the samples

See the [samples README](https://github.com/temporal-community/temporal-aspire/blob/main/samples/README.md) for local and Temporal Cloud configuration and startup instructions.

## Resources

- [Temporal .NET SDK](https://github.com/temporalio/sdk-dotnet)
- [Temporal .NET SDK documentation](https://docs.temporal.io/develop/dotnet/)
- [Temporal production deployment documentation](https://docs.temporal.io/production-deployment)

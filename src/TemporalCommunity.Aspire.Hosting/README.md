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

`AddTemporalDevContainer` requires Aspire 13.6.0 or later. Aspire allocates free host ports by default while Temporal keeps its standard internal ports: 7233 for gRPC, 8233 for the UI, and 9233 for metrics. Referenced projects receive the allocated addresses automatically.

Configure non-default values when fixed host ports are required:

```csharp
var temporal = builder.AddTemporalDevContainer("temporal", options =>
{
    options.TargetHost = "0.0.0.0:7399";
    options.UIPort = 8399;
    options.MetricsPort = 9399;
});
```

## Dashboard terminal

Add `.WithCliTerminal()` to any local server mode to show **Open Temporal CLI** in the Aspire Dashboard:

```csharp
var temporal = builder.AddTemporalDevContainer("temporal")
    .WithCliTerminal();
```

The same extension works with `AddTemporalCliServer` and `AddTemporalLocalDevServer`. It opens an interactive shell with `TEMPORAL_ADDRESS` and `TEMPORAL_NAMESPACE` configured for the running server. Run commands such as `temporal workflow list` directly in that shell. The command is enabled only while the server is running and healthy.

Container terminals use the CLI bundled in the image through Aspire's selected Docker or Podman runtime; no host Temporal CLI installation is needed. CLI and SDK-managed local terminals use a host shell and require the [Temporal CLI](https://docs.temporal.io/cli/setup-cli) on `PATH`. Temporal Cloud resources do not expose this terminal command.

The AppHost must enable the Aspire CLI bundle, which supplies Dashboard terminal support:

```xml
<PropertyGroup>
    <AspireUseCliBundle>true</AspireUseCliBundle>
</PropertyGroup>
```

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
    temporalUiAddress,
    configure: options => options.EnableHealthCheck = true);

builder.AddProject<Projects.Worker>("worker")
    .WithReference(temporal);
```

Use secret parameters for API keys and codec credentials. A UI address adds a **Temporal Dashboard** link in Aspire. `WithReference` supplies the Temporal connection environment variables to consuming projects, which can load them with `ClientEnvConfig.LoadClientConnectOptions()`.

`EnableHealthCheck` verifies the endpoint, TLS, and credentials with `GetSystemInfo`; it does not verify worker or task-queue health.

## Persist development state

Set Temporal's dev-server database filename to persist local development state:

```csharp
Directory.CreateDirectory("../.temporal");

var temporal = builder.AddTemporalLocalDevServer("temporal", options =>
{
    options.DevServerOptions.DatabaseFilename = "../.temporal/temporal.db";
});
```

For CLI and container servers, use `.WithDataVolume(name)`:

```csharp
var temporal = builder.AddTemporalDevContainer("temporal")
    .WithDataVolume("temporal-data")
    .WithCliTerminal();
```

The same persistence extension works with `AddTemporalCliServer`. Aspire resolves a directory scoped to the CLI resource and storage name beneath its local store; a container uses `/home/temporal` in the named volume. Temporal writes `temporal.db` into that directory. Keep the AppHost location, resource name, and storage name stable to reuse the data. The CLI store is normally under the AppHost's `obj/.aspire` directory, so removing `obj` also removes that data.

Choose either `.WithDataVolume()` or an explicit `DevServerOptions.DatabaseFilename`; configuring both is rejected. Existing explicit filenames and container mounts continue to work. CLI directories and container volumes are separate storage: switching modes does not migrate a database automatically. SDK-managed local persistence continues to use the filename shown above.

`DevServerOptions` is marked unstable by the Temporal .NET SDK and may change in future SDK versions.

When `UI = false`, the resource does not publish a dashboard URL or `TEMPORAL_UI_ADDRESS`. Development resources expose `/metrics`; container host ports are allocated unless non-default ports are configured explicitly.

## Resources

- [Temporal .NET SDK](https://github.com/temporalio/sdk-dotnet)
- [Temporal .NET SDK documentation](https://docs.temporal.io/develop/dotnet/)
- [Temporal production deployment documentation](https://docs.temporal.io/production-deployment)

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

## Resources

- [Temporal .NET SDK](https://github.com/temporalio/sdk-dotnet)
- [Temporal .NET SDK documentation](https://docs.temporal.io/develop/dotnet/)
- [Temporal production deployment documentation](https://docs.temporal.io/production-deployment)

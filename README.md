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

builder.AddProject<Projects.Worker>("worker")
    .WithReference(temporal);
```

Use secret parameters for API keys and codec credentials. A UI address adds a **Temporal Dashboard** link in Aspire. `WithReference` supplies the Temporal connection environment variables to consuming projects, which can load them with `ClientEnvConfig.LoadClientConnectOptions()`.

`EnableHealthCheck` verifies the endpoint, TLS, and credentials with `GetSystemInfo`; it does not verify worker or task-queue health. See the [samples README](samples/README.md) for configuration and startup instructions.

## Persist local development state

The local development server uses an in-memory database by default. To persist state between runs, set Temporal's dev-server database filename:

```csharp
Directory.CreateDirectory("../.temporal");

var temporal = builder.AddTemporalLocalDevServer("temporal", options =>
{
    options.DevServerOptions.DatabaseFilename = "../.temporal/temporal.db";
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

When `UI = false`, the resource does not publish a dashboard URL or `TEMPORAL_UI_ADDRESS`. Development resources expose `/metrics` on the configured metrics port.

## Run the samples

Both samples use the same worker, client, and workflow projects.

Local development server:

```bash
aspire start --apphost samples/SampleAppHost/SampleAppHost.csproj --non-interactive
```

Temporal Cloud:

```bash
cd samples/SampleCloudAppHost
cp .secrets.env.example .secrets.env
# Add your Temporal Cloud values to .secrets.env.
source .secrets.env
aspire start --non-interactive
```

See the [samples README](samples/README.md) for details.

## Resources

- [Temporal .NET SDK](https://github.com/temporalio/sdk-dotnet)
- [Temporal .NET SDK documentation](https://docs.temporal.io/develop/dotnet/)
- [Temporal production deployment documentation](https://docs.temporal.io/production-deployment)

# Hosting sample

This folder contains samples for the projects in this repository.

## Local development server sample

`SampleAppHost` shows how to use `TemporalCommunity.Aspire.Hosting` from an Aspire AppHost. It starts a local Temporal development server, injects Temporal connection environment variables into a worker and client, and runs a simple workflow.

The local dev server configures Temporal's `DevServerOptions.DatabaseFilename` to use `samples/.temporal/temporal.db` for persisted state.

The worker and client both load connection settings with `ClientEnvConfig.LoadClientConnectOptions()`, so the same consumer pattern works with `AddTemporalCloud` and `TEMPORAL_API_KEY`.

For production, keep the AppHost pointed at an externally managed Temporal endpoint such as Temporal Cloud. Run at least two workers per task queue, tune task slots, sticky cache, and pollers from load tests, use Worker Versioning for workflow-code rollouts, and configure graceful shutdown. Monitor CPU/memory, Schedule-to-Start latency, available task slots, and request failures/latency together. Use KEDA or Temporal Worker Controller scale-to-zero only when the workload can tolerate worker cold-start latency.

```bash
aspire start --apphost samples/SampleAppHost/SampleAppHost.csproj --non-interactive
```

## Temporal Cloud sample

`SampleCloudAppHost` connects the same worker and client projects to a Temporal Cloud namespace. It declares the Temporal endpoint, namespace, API key, and Cloud UI address as Aspire parameters. The API key is secret; the UI address is deliberately not, because Aspire uses it to display a **Temporal Cloud** link in the dashboard and injects it as `TEMPORAL_UI_ADDRESS` into referenced projects.

The sample enables the optional Cloud health check so the dashboard verifies an authenticated `GetSystemInfo` call before starting the worker and client. It verifies endpoint reachability, TLS, and credentials—not task-queue polling or worker capacity.

Copy the included template to a gitignored `.secrets.env` file, replace the placeholder values, and source it before starting the sample:

```bash
cp samples/SampleCloudAppHost/.secrets.env.example \
  samples/SampleCloudAppHost/.secrets.env

# Edit samples/SampleCloudAppHost/.secrets.env with your Temporal Cloud values.

source samples/SampleCloudAppHost/.secrets.env

aspire start --apphost samples/SampleCloudAppHost/SampleCloudAppHost.csproj --non-interactive
```

`temporal-api-key` remains a secret Aspire parameter even when its local value is sourced from the environment. The address, namespace, and UI address are ordinary external configuration; the UI URL should not be put in a deployment secret store. Do not commit API keys. If your deployment uses a payload codec, add `Parameters__temporal_codec_auth` to `.secrets.env`, declare a matching secret parameter, and pass it as the sixth argument to `AddTemporalCloud`.

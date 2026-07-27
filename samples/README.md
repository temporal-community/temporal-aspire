# TemporalCommunity.Aspire.Hosting samples

This folder contains samples for the projects in this repository.

## Local development server sample

`TemporalCommunity.Aspire.Hosting.SampleAppHost` shows how to use `TemporalCommunity.Aspire.Hosting` from an Aspire AppHost. It starts a local Temporal development server, injects Temporal connection environment variables into a worker and client, and runs a simple workflow.

The local dev server configures Temporal's `DevServerOptions.DatabaseFilename` to use `samples/.temporal/temporal.db` for persisted state.

The worker and client both load connection settings with `ClientEnvConfig.LoadClientConnectOptions()`, so the same consumer pattern works with `AddTemporalCloud` and `TEMPORAL_API_KEY`.

For production, keep the AppHost pointed at an externally managed Temporal endpoint such as Temporal Cloud. Run at least two workers per task queue, tune task slots, sticky cache, and pollers from load tests, use Worker Versioning for workflow-code rollouts, and configure graceful shutdown. Monitor CPU/memory, Schedule-to-Start latency, available task slots, and request failures/latency together. Use KEDA or Temporal Worker Controller scale-to-zero only when the workload can tolerate worker cold-start latency.

```bash
aspire start --apphost samples/TemporalCommunity.Aspire.Hosting.SampleAppHost/TemporalCommunity.Aspire.Hosting.SampleAppHost.csproj
```

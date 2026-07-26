# TemporalCommunity.Aspire.Hosting samples

This folder contains samples for the projects in this repository.

## Local development server sample

`TemporalCommunity.Aspire.Hosting.SampleAppHost` shows how to use `TemporalCommunity.Aspire.Hosting` from an Aspire AppHost. It starts a local Temporal development server, injects Temporal connection environment variables into a worker and client, and runs a simple workflow.

The local dev server configures Temporal's `DevServerOptions.DatabaseFilename` to use `samples/.temporal/temporal.db` for persisted state.

The worker and client both load connection settings with `ClientEnvConfig.LoadClientConnectOptions()`, so the same consumer pattern works with `AddTemporalCloud` and `TEMPORAL_API_KEY`.

```bash
aspire start --project samples/TemporalCommunity.Aspire.Hosting.SampleAppHost/TemporalCommunity.Aspire.Hosting.SampleAppHost.csproj
```

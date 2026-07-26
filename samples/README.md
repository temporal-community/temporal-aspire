# TemporalCommunity.Aspire.Hosting samples

This folder contains samples for the projects in this repository.

## Local development server sample

`TemporalCommunity.Aspire.Hosting.SampleAppHost` shows how to use `TemporalCommunity.Aspire.Hosting` from an Aspire AppHost. It starts a local Temporal development server, injects Temporal connection environment variables into a worker and client, and runs a simple workflow.

```bash
aspire start --project samples/TemporalCommunity.Aspire.Hosting.SampleAppHost/TemporalCommunity.Aspire.Hosting.SampleAppHost.csproj
```

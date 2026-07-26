using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Temporalio.Common.EnvConfig;
using Temporalio.Extensions.Hosting;
using TemporalCommunity.Aspire.Hosting.SampleWorkflow;

var builder = Host.CreateApplicationBuilder(args);
var connectOptions = ClientEnvConfig.LoadClientConnectOptions();

builder.Services.AddHostedTemporalWorker(
    clientTargetHost: connectOptions.TargetHost ?? "localhost:7233",
    clientNamespace: connectOptions.Namespace,
    taskQueue: "sample-task-queue")
    .AddWorkflow<SimpleWorkflow>()
    .AddStaticActivities<SimpleActivities>();

await builder.Build().RunAsync();

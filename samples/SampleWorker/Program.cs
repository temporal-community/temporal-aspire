using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TemporalCommunity.Aspire.Hosting.SampleWorkflow;
using Temporalio.Common.EnvConfig;
using Temporalio.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
var connectOptions = ClientEnvConfig.LoadClientConnectOptions();

builder.Services.AddHostedTemporalWorker(
    taskQueue: SampleWorkflowConstants.TaskQueue)
    .ConfigureOptions(options => options.ClientOptions = connectOptions)
    .AddWorkflow<SimpleWorkflow>()
    .AddStaticActivities<SimpleActivities>();

await builder.Build().RunAsync();

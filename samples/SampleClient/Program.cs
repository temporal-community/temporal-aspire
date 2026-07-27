using TemporalCommunity.Aspire.Hosting.SampleWorkflow;
using Temporalio.Client;
using Temporalio.Common.EnvConfig;

var connectOptions = ClientEnvConfig.LoadClientConnectOptions();

Console.WriteLine($"Connecting to Temporal at {connectOptions.TargetHost} in namespace {connectOptions.Namespace}...");

var client = await TemporalClient.ConnectAsync(connectOptions);
var workflowId = $"sample-workflow-{Guid.NewGuid():N}";

var result = await client.ExecuteWorkflowAsync(
    (SimpleWorkflow workflow) => workflow.RunAsync("Aspire"),
    new(id: workflowId, taskQueue: SampleWorkflowConstants.TaskQueue));

Console.WriteLine(result);

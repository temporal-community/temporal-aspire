using Temporalio.Workflows;

namespace TemporalCommunity.Aspire.Hosting.SampleWorkflow;

[Workflow]
public class SimpleWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(string name)
    {
        return await Workflow.ExecuteActivityAsync(
            () => SimpleActivities.SayHelloAsync(name),
            new() { StartToCloseTimeout = TimeSpan.FromMinutes(1) });
    }
}

using Temporalio.Activities;

namespace TemporalCommunity.Aspire.Hosting.SampleWorkflow;

public class SimpleActivities
{
    [Activity]
    public static async Task<string> SayHelloAsync(string name)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        return $"Hello, {name}, from Temporal!";
    }
}

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using TemporalCommunity.Aspire.Hosting;
using Xunit;

namespace TemporalCommunity.Aspire.Hosting.Tests;

public class TemporalResourceExtensionsTests
{
    [Fact]
    public void AddTemporalDevContainer_HeadlessModeDoesNotPublishUiEndpoint()
    {
        var appBuilder = DistributedApplication.CreateBuilder([]);

        var temporal = appBuilder.AddTemporalDevContainer("temporal", options => options.UI = false);

        var endpointNames = temporal.Resource.Annotations
            .OfType<EndpointAnnotation>()
            .Select(endpoint => endpoint.Name)
            .ToList();

        Assert.DoesNotContain(TemporalResourceConstants.UIEndpointName, endpointNames);
        Assert.Contains(TemporalResourceConstants.ServiceEndpointName, endpointNames);
        Assert.Contains(TemporalResourceConstants.MetricsEndpointName, endpointNames);
        Assert.DoesNotContain(
            temporal.Resource.Annotations,
            annotation => annotation is ResourceUrlAnnotation or ResourceUrlsCallbackAnnotation);
    }

    [Fact]
    public async Task WithReference_HeadlessContainerDoesNotInjectUiAddress()
    {
        var appBuilder = DistributedApplication.CreateBuilder([]);
        var temporal = appBuilder.AddTemporalDevContainer("temporal", options => options.UI = false);
        var worker = appBuilder.AddContainer("worker", "busybox").WithReference(temporal);
        var environmentVariables = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run),
            environmentVariables,
            CancellationToken.None);

        var annotation = worker.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>().Last();
        await annotation.Callback(context);

        Assert.False(environmentVariables.ContainsKey("TEMPORAL_UI_ADDRESS"));
        Assert.Contains("TEMPORAL_ADDRESS", environmentVariables.Keys);
    }

    [Fact]
    public void AddTemporalLocalDevServer_HeadlessModeDoesNotPublishUiEndpoint()
    {
        var appBuilder = DistributedApplication.CreateBuilder([]);

        var temporal = appBuilder.AddTemporalLocalDevServer("temporal", options => options.UI = false);

        var endpointNames = temporal.Resource.Annotations
            .OfType<EndpointAnnotation>()
            .Select(endpoint => endpoint.Name)
            .ToList();

        Assert.DoesNotContain(TemporalResourceConstants.UIEndpointName, endpointNames);
        Assert.Contains(TemporalResourceConstants.ServiceEndpointName, endpointNames);
        Assert.Contains(TemporalResourceConstants.MetricsEndpointName, endpointNames);
    }
}

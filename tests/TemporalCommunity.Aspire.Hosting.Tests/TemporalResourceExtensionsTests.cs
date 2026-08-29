using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using TemporalCommunity.Aspire.Hosting;
using Xunit;

namespace TemporalCommunity.Aspire.Hosting.Tests;

public class TemporalResourceExtensionsTests
{
    [Fact]
    public void AddTemporalDevContainer_DefaultPortsUseDynamicProxylessHostBindings()
    {
        var appBuilder = DistributedApplication.CreateBuilder([]);

        var temporal = appBuilder.AddTemporalDevContainer("temporal");
        var endpoints = temporal.Resource.Annotations.OfType<EndpointAnnotation>().ToList();

        var service = endpoints.Single(endpoint => endpoint.Name == TemporalResourceConstants.ServiceEndpointName);
        var ui = endpoints.Single(endpoint => endpoint.Name == TemporalResourceConstants.UIEndpointName);
        var metrics = endpoints.Single(endpoint => endpoint.Name == TemporalResourceConstants.MetricsEndpointName);

        Assert.Null(service.Port);
        Assert.Equal(TemporalResourceConstants.DefaultServiceEndpointPort, service.TargetPort);
        Assert.False(service.IsProxied);

        Assert.Null(ui.Port);
        Assert.Equal(TemporalResourceConstants.DefaultUIEndpointPort, ui.TargetPort);
        Assert.False(ui.IsProxied);

        Assert.Null(metrics.Port);
        Assert.Equal(TemporalResourceConstants.DefaultMetricsEndpointPort, metrics.TargetPort);
        Assert.False(metrics.IsProxied);
    }

    [Fact]
    public void AddTemporalDevContainer_NonDefaultPortsRemainExplicitHostBindings()
    {
        var appBuilder = DistributedApplication.CreateBuilder([]);

        var temporal = appBuilder.AddTemporalDevContainer("temporal", options =>
        {
            options.TargetHost = "0.0.0.0:17333";
            options.UIPort = 18333;
            options.MetricsPort = 19333;
        });
        var endpoints = temporal.Resource.Annotations.OfType<EndpointAnnotation>().ToList();

        var service = endpoints.Single(endpoint => endpoint.Name == TemporalResourceConstants.ServiceEndpointName);
        var ui = endpoints.Single(endpoint => endpoint.Name == TemporalResourceConstants.UIEndpointName);
        var metrics = endpoints.Single(endpoint => endpoint.Name == TemporalResourceConstants.MetricsEndpointName);

        Assert.Equal(17333, service.Port);
        Assert.Equal(TemporalResourceConstants.DefaultServiceEndpointPort, service.TargetPort);
        Assert.Equal(18333, ui.Port);
        Assert.Equal(TemporalResourceConstants.DefaultUIEndpointPort, ui.TargetPort);
        Assert.Equal(19333, metrics.Port);
        Assert.Equal(TemporalResourceConstants.DefaultMetricsEndpointPort, metrics.TargetPort);
    }

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

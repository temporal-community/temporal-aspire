using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using TemporalCommunity.Aspire.Hosting;
using Xunit;

namespace TemporalCommunity.Aspire.Hosting.Tests;

public class TemporalCloudResourceTests
{
    [Fact]
    public void AddTemporalCloud_KeepsStringNamespaceCompatibility()
    {
        var appBuilder = DistributedApplication.CreateBuilder([]);
        var address = appBuilder.AddParameter("temporal-address");

        var temporal = appBuilder.AddTemporalCloud("temporal", address, "orders.prod");

        Assert.Equal("orders.prod", temporal.Resource.Options.Namespace);
    }

    [Fact]
    public void AddTemporalCloud_AcceptsParameterizedNamespace()
    {
        var appBuilder = DistributedApplication.CreateBuilder([]);
        var address = appBuilder.AddParameter("temporal-address");
        var @namespace = appBuilder.AddParameter("temporal-namespace");

        var temporal = appBuilder.AddTemporalCloud("temporal", address, @namespace);

        Assert.Same(@namespace.Resource, temporal.Resource.Options.Namespace);
    }

    [Fact]
    public void AddTemporalCloud_AddsUrlAnnotation_ForStringUiAddress()
    {
        var appBuilder = DistributedApplication.CreateBuilder([]);

        var temporal = appBuilder.AddTemporalCloud(
            "temporal",
            ReferenceExpression.Create($"orders.prod.tmprl.cloud:7233"),
            options =>
            {
                options.Namespace = "orders.prod";
                options.UIAddress = "https://cloud.temporal.io/namespaces/orders.prod";
            });

        var url = Assert.Single(temporal.Resource.Annotations.OfType<ResourceUrlAnnotation>());
        Assert.Equal("Temporal Cloud", url.DisplayText);
        Assert.Equal("https://cloud.temporal.io/namespaces/orders.prod", url.Url);
    }

    [Fact]
    public void AddTemporalCloud_AddsUrlAnnotation_ForParameterizedUiAddress()
    {
        var appBuilder = DistributedApplication.CreateBuilder([]);
        var uiAddress = appBuilder.AddParameter("temporal-ui-address");

        var temporal = appBuilder.AddTemporalCloud(
            "temporal",
            ReferenceExpression.Create($"orders.prod.tmprl.cloud:7233"),
            options =>
            {
                options.Namespace = "orders.prod";
                options.UIAddress = uiAddress.Resource;
            });

        Assert.Single(temporal.Resource.Annotations.OfType<ResourceUrlsCallbackAnnotation>());
    }

    [Fact]
    public void AddTemporalCloud_DoesNotAddUrlAnnotation_WhenUiAddressIsOmitted()
    {
        var appBuilder = DistributedApplication.CreateBuilder([]);

        var temporal = appBuilder.AddTemporalCloud(
            "temporal",
            ReferenceExpression.Create($"orders.prod.tmprl.cloud:7233"),
            options => options.Namespace = "orders.prod");

        Assert.Empty(temporal.Resource.Annotations.OfType<ResourceUrlAnnotation>());
    }

    [Fact]
    public void TemporalCloudResource_IsConnectionStringResource_NotServiceDiscoveryResource()
    {
        var resource = new TemporalCloudResource(
            "temporal-cloud",
            ReferenceExpression.Create($"orders.prod.tmprl.cloud:7233"));

        Assert.IsAssignableFrom<IResourceWithConnectionString>(resource);
        Assert.False(resource is IResourceWithServiceDiscovery);
    }

    [Fact]
    public async Task TemporalCloudResource_ReturnsConfiguredConnectionStringExpression()
    {
        var resource = new TemporalCloudResource(
            "temporal-cloud",
            ReferenceExpression.Create($"orders.prod.tmprl.cloud:7233"));

        var connectionString = await resource.ConnectionStringExpression.GetValueAsync(CancellationToken.None);

        Assert.Equal("orders.prod.tmprl.cloud:7233", connectionString);
    }
}

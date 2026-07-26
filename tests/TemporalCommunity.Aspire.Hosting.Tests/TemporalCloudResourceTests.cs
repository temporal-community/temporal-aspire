using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using TemporalCommunity.Aspire.Hosting;
using Xunit;

namespace TemporalCommunity.Aspire.Hosting.Tests;

public class TemporalCloudResourceTests
{
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

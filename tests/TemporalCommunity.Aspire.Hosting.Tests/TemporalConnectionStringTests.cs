using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TemporalCommunity.Aspire.Hosting.Tests;

public class TemporalConnectionStringTests
{
    [Theory]
    [InlineData(false, "temporal-cli-server", "temporal_cli_server")]
    [InlineData(true, "temporal-container", "temporal_container")]
    public async Task ConnectionStringReference_EmitsPortableAliasForDefaultNames(
        bool container, string name, string portableName)
    {
        var builder = DistributedApplication.CreateBuilder([]);
        IResourceBuilder<IResourceWithConnectionString> server = container
            ? builder.AddTemporalDevContainer()
            : builder.AddTemporalCliServer("temporal-cli-server", null, () => true);
        var endpoint = server.Resource.Annotations.OfType<EndpointAnnotation>()
            .Single(annotation => annotation.Name == TemporalResourceConstants.ServiceEndpointName);
        endpoint.AllocatedEndpoint = new AllocatedEndpoint(endpoint, "localhost", 17233);
        var dependent = builder.AddExecutable("dependent", "unused", ".")
            .WithReference(server);
        using var services = new ServiceCollection().BuildServiceProvider();
        var executionContext = new DistributedApplicationExecutionContext(
            new DistributedApplicationExecutionContextOptions(DistributedApplicationOperation.Run) { Services = services });

        var configuration = await ExecutionConfigurationBuilder.Create(dependent.Resource)
            .WithEnvironmentVariablesConfig().BuildAsync(executionContext, NullLogger.Instance);

        Assert.Null(configuration.Exception);
        var environment = configuration.EnvironmentVariables.ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal("localhost:17233", environment[$"ConnectionStrings__{name}"]);
        Assert.Equal("localhost:17233", environment[$"ConnectionStrings__{portableName}"]);
    }
}

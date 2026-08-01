using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using TemporalCommunity.Aspire.Hosting;
using Temporalio.Client;
using Xunit;

namespace TemporalCommunity.Aspire.Hosting.Tests;

public class TemporalHealthCheckHelperTests
{
    [Fact]
    public async Task AccessorReturnsNull_BeforeConnectionStringEventFires()
    {
        // Regression: the old design attempted to connect inside the ConnectionStringAvailableEvent
        // callback and threw when the port wasn't bound yet, causing FailedToStart. The new design
        // has the accessor return null (Unhealthy "not yet initialized") until the first successful
        // connect, either during warm-up retries or on a later health probe.
        //
        // This test ensures that calling the accessor immediately after registration — before any
        // Aspire event has fired — returns null rather than throwing.
        var appBuilder = DistributedApplication.CreateBuilder([]);
        var resource = new TemporalCliServerResource("test-temporal");

        var accessor = TemporalHealthCheckHelper.RegisterCachedClientAccessor(
            appBuilder, resource, "default");

        var client = await accessor(CancellationToken.None);

        Assert.Null(client);
    }

    [Fact]
    public async Task AccessorIsIdempotent_WhenCalledMultipleTimes_BeforeEventFires()
    {
        // The accessor must be safe to call repeatedly (health checks probe on every interval).
        // It must never throw when no connection string has been published yet.
        var appBuilder = DistributedApplication.CreateBuilder([]);
        var resource = new TemporalCliServerResource("test-temporal");

        var accessor = TemporalHealthCheckHelper.RegisterCachedClientAccessor(
            appBuilder, resource, "default");

        for (var i = 0; i < 3; i++)
        {
            var client = await accessor(CancellationToken.None);
            Assert.Null(client);
        }
    }

    [Fact]
    public async Task CloudAccessor_ResolvesParametersAndConnectsOnlyOnceForParallelProbes()
    {
        var appBuilder = DistributedApplication.CreateBuilder([]);
        var address = appBuilder.AddParameter("temporal-address", "orders.prod.tmprl.cloud:7233");
        var @namespace = appBuilder.AddParameter("temporal-namespace", "orders.prod");
        var apiKey = appBuilder.AddParameter("temporal-api-key", "test-api-key", secret: true);
        var resource = new TemporalCloudResource("temporal", ReferenceExpression.Create($"{address.Resource}"));
        resource.Options.Namespace = @namespace.Resource;
        resource.Options.ApiKey = apiKey.Resource;

        TemporalClientConnectOptions? capturedOptions = null;
        var connectionCount = 0;
        var client = DispatchProxy.Create<ITemporalClient, ThrowingTemporalClientProxy>();
        var accessor = TemporalHealthCheckHelper.RegisterCloudClientAccessor(
            resource,
            (options, _) =>
            {
                connectionCount++;
                capturedOptions = options;
                return Task.FromResult(client);
            });

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => accessor(CancellationToken.None)));

        Assert.Equal(1, connectionCount);
        Assert.All(results, result => Assert.Same(client, result));
        Assert.NotNull(capturedOptions);
        Assert.Equal("orders.prod.tmprl.cloud:7233", capturedOptions.TargetHost);
        Assert.Equal("orders.prod", capturedOptions.Namespace);
        Assert.Equal("test-api-key", capturedOptions.ApiKey);
        Assert.NotNull(capturedOptions.Tls);
    }

    [Fact]
    public async Task CloudAccessor_CancelledWaiterDoesNotInterruptInitialization()
    {
        var resource = new TemporalCloudResource(
            "temporal",
            ReferenceExpression.Create($"orders.prod.tmprl.cloud:7233"));
        resource.Options.Namespace = "orders.prod";

        var connectionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishConnection = new TaskCompletionSource<ITemporalClient>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionCount = 0;
        var client = DispatchProxy.Create<ITemporalClient, ThrowingTemporalClientProxy>();
        var accessor = TemporalHealthCheckHelper.RegisterCloudClientAccessor(
            resource,
            (_, _) =>
            {
                connectionCount++;
                connectionStarted.SetResult();
                return finishConnection.Task;
            });

        var initializingProbe = accessor(CancellationToken.None);
        await connectionStarted.Task.ConfigureAwait(true);

        using var cancellationSource = new CancellationTokenSource();
        var cancelledProbe = accessor(cancellationSource.Token);
        await cancellationSource.CancelAsync().ConfigureAwait(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledProbe);

        finishConnection.SetResult(client);
        Assert.Same(client, await initializingProbe.ConfigureAwait(true));
        Assert.Same(client, await accessor(CancellationToken.None));
        Assert.Equal(1, connectionCount);
    }

    [Fact]
    public async Task CloudAccessor_ReturnsNullWhenRequiredValuesAreMissing()
    {
        var resource = new TemporalCloudResource(
            "temporal",
            ReferenceExpression.Create($"orders.prod.tmprl.cloud:7233"));
        resource.Options.Namespace = string.Empty;

        var accessor = TemporalHealthCheckHelper.RegisterCloudClientAccessor(
            resource,
            (_, _) => throw new InvalidOperationException("A client must not be created without a namespace."));

        Assert.Null(await accessor(CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task CloudAccessor_DoesNotCacheFailedConnections()
    {
        var resource = new TemporalCloudResource(
            "temporal",
            ReferenceExpression.Create($"orders.prod.tmprl.cloud:7233"));
        resource.Options.Namespace = "orders.prod";

        var connectionCount = 0;
        var client = DispatchProxy.Create<ITemporalClient, ThrowingTemporalClientProxy>();
        var accessor = TemporalHealthCheckHelper.RegisterCloudClientAccessor(
            resource,
            (_, _) =>
            {
                connectionCount++;
                return connectionCount == 1
                    ? Task.FromException<ITemporalClient>(new InvalidOperationException("Unavailable"))
                    : Task.FromResult(client);
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => accessor(CancellationToken.None)).ConfigureAwait(true);

        Assert.Same(client, await accessor(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(2, connectionCount);
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by DispatchProxy.")]
    [SuppressMessage("Performance", "CA1852:Seal internal types", Justification = "DispatchProxy requires an unsealed proxy base type.")]
    private class ThrowingTemporalClientProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException($"Unexpected Temporal client member: {targetMethod?.Name}");
    }
}

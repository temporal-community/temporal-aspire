using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using TemporalCommunity.Aspire.Hosting;
using Temporalio.Client;
using Xunit;

namespace TemporalCommunity.Aspire.Hosting.Tests;

public class TemporalHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy_WhenClientAccessorReturnsNull()
    {
        // Regression: before the lazy-reconnect refactor, a null client meant the resource stayed
        // permanently Unhealthy with no path to recovery. The fix ensures the description is
        // "not yet initialized" (a transient state) rather than a hard failure.
        var healthCheck = new TemporalHealthCheck(_ => Task.FromResult<ITemporalClient?>(null));

        var result = await healthCheck.CheckHealthAsync(MakeContext(healthCheck), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("not yet initialized", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy_WhenAccessorAlwaysReturnsNull_StatusIsTransient()
    {
        // A second probe with a still-null client also returns Unhealthy, confirming the accessor
        // is called on every probe (lazy, not just once).
        var callCount = 0;
        var healthCheck = new TemporalHealthCheck(_ =>
        {
            callCount++;
            return Task.FromResult<ITemporalClient?>(null);
        });

        await healthCheck.CheckHealthAsync(MakeContext(healthCheck), CancellationToken.None);
        await healthCheck.CheckHealthAsync(MakeContext(healthCheck), CancellationToken.None);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task CheckHealthAsync_PropagatesCancellation_WhenTokenCancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var healthCheck = new TemporalHealthCheck(ct =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<ITemporalClient?>(null);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => healthCheck.CheckHealthAsync(MakeContext(healthCheck), cts.Token));
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy_WhenClientAccessorFails()
    {
        var exception = new InvalidOperationException("Connection failed");
        var healthCheck = new TemporalHealthCheck(_ => Task.FromException<ITemporalClient?>(exception));

        var result = await healthCheck.CheckHealthAsync(MakeContext(healthCheck), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Temporal health check failed; verify credentials.", result.Description);
        Assert.Same(exception, result.Exception);
    }

    [Fact]
    public async Task CheckHealthAsync_IncludesSafeTroubleshootingHint_WhenServerCallFails()
    {
        var client = DispatchProxy.Create<ITemporalClient, ThrowingTemporalClientProxy>();
        var healthCheck = new TemporalHealthCheck(_ => Task.FromResult<ITemporalClient?>(client));

        var result = await healthCheck.CheckHealthAsync(MakeContext(healthCheck), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Temporal health check failed; verify credentials.", result.Description);
        Assert.IsType<NotSupportedException>(result.Exception);
    }

    private static HealthCheckContext MakeContext(IHealthCheck instance) =>
        new() { Registration = new HealthCheckRegistration("temporal", instance, HealthStatus.Unhealthy, null) };

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by DispatchProxy.")]
    [SuppressMessage("Performance", "CA1852:Seal internal types", Justification = "DispatchProxy requires an unsealed proxy base type.")]
    private class ThrowingTemporalClientProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException($"Unexpected Temporal client member: {targetMethod?.Name}");
    }
}

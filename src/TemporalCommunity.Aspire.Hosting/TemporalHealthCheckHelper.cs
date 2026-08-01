using Aspire.Hosting.ApplicationModel;
using System.Diagnostics.CodeAnalysis;
using Temporalio.Client;

namespace TemporalCommunity.Aspire.Hosting;

/// <summary>
/// Helper for registering cached Temporal client accessors for health checks.
/// </summary>
internal static class TemporalHealthCheckHelper
{
    /// <summary>
    /// Creates an accessor for an externally managed Temporal Cloud namespace. Resolution and connection creation are
    /// single-flight so concurrent dashboard probes cannot establish duplicate clients or race value providers.
    /// </summary>
    /// <param name="resource">The Temporal Cloud resource to monitor.</param>
    /// <param name="connectAsync">The client factory. Intended for tests; production uses <see cref="TemporalClient.ConnectAsync(TemporalClientConnectOptions)"/>.</param>
    /// <returns>A function that returns the cached client once connectivity is established.</returns>
    internal static Func<CancellationToken, Task<ITemporalClient?>> RegisterCloudClientAccessor(
        TemporalCloudResource resource,
        Func<TemporalClientConnectOptions, CancellationToken, Task<ITemporalClient>>? connectAsync = null)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return new CloudClientAccessor(resource, connectAsync).GetClientAsync;
    }

    /// <summary>
    /// Subscribes to <see cref="ConnectionStringAvailableEvent"/> for a CLI or container resource,
    /// creates a <see cref="ITemporalClient"/> once the endpoint is available, and returns an accessor
    /// that the health check can call on every probe.
    /// The cached client is replaced on each subsequent event so restarts are covered.
    /// </summary>
    /// <param name="builder">The distributed application builder for subscribing to events.</param>
    /// <param name="resource">The Temporal resource being monitored for connection string availability.</param>
    /// <param name="namespace">The Temporal namespace for client connections.</param>
    /// <returns>A function that accepts a cancellation token and returns the cached ITemporalClient or null if not yet connected.</returns>
    internal static Func<CancellationToken, Task<ITemporalClient?>> RegisterCachedClientAccessor(
        IDistributedApplicationBuilder builder,
        IResource resource,
        string @namespace)
    {
        ITemporalClient? cachedClient = null;
        string? hostPort = null;

        async Task<ITemporalClient?> EnsureClientConnectedAsync(CancellationToken cancellationToken)
        {
            if (cachedClient is not null)
                return cachedClient;

            if (string.IsNullOrEmpty(hostPort))
                return null;

            try
            {
                cachedClient = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions
                {
                    Namespace = @namespace,
                    TargetHost = hostPort
                });
                return cachedClient;
            }
            catch (InvalidOperationException)
            {
                cachedClient = null;
                return null;
            }
        }

        builder.Eventing.Subscribe<ConnectionStringAvailableEvent>(resource, async (@event, _) =>
        {
            try
            {
                if (!@event.Resource.TryGetEndpoints(out var endpoints))
                    return;

                var serviceEndpoint = endpoints.Single(e => e.Name == TemporalResourceConstants.ServiceEndpointName);
                hostPort = $"{serviceEndpoint.TargetHost}:{serviceEndpoint.Port}";
                cachedClient = null;

                // The endpoint can be published before Temporal is accepting connections.
                // Retry a few times but never throw from this callback.
                const int maxAttempts = 30;
                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    var client = await EnsureClientConnectedAsync(CancellationToken.None);
                    if (client is not null)
                        break;

                    if (attempt < maxAttempts)
                        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
                }
            }
            catch
            {
                // Resource startup must not fail because client warm-up failed.
                cachedClient = null;
            }
        });

        return EnsureClientConnectedAsync;
    }

    private static ValueTask<string?> ResolveValueAsync(object? value, CancellationToken cancellationToken) =>
        value switch
        {
            null => ValueTask.FromResult<string?>(null),
            string text => ValueTask.FromResult<string?>(text),
            IValueProvider valueProvider => valueProvider.GetValueAsync(cancellationToken),
            _ => ValueTask.FromResult<string?>(null)
        };

    [SuppressMessage(
        "Design",
        "CA1001:Types that own disposable fields should be disposable",
        Justification = "The accessor and synchronization gate live for the AppHost health-check registration lifetime.")]
    private sealed class CloudClientAccessor(
        TemporalCloudResource resource,
        Func<TemporalClientConnectOptions, CancellationToken, Task<ITemporalClient>>? connectAsync)
    {
        private readonly SemaphoreSlim initializationGate = new(1, 1);
        private readonly Func<TemporalClientConnectOptions, CancellationToken, Task<ITemporalClient>> connectAsync =
            connectAsync ?? DefaultConnectAsync;
        private ITemporalClient? cachedClient;

        internal async Task<ITemporalClient?> GetClientAsync(CancellationToken cancellationToken)
        {
            var existingClient = Volatile.Read(ref cachedClient);
            if (existingClient is not null)
                return existingClient;

            await initializationGate.WaitAsync(cancellationToken);
            try
            {
                existingClient = Volatile.Read(ref cachedClient);
                if (existingClient is not null)
                    return existingClient;

                var targetHost = await resource.AddressExpression.GetValueAsync(cancellationToken);
                var @namespace = await ResolveValueAsync(resource.Options.Namespace, cancellationToken);
                var apiKey = await ResolveValueAsync(resource.Options.ApiKey, cancellationToken);
                if (string.IsNullOrWhiteSpace(targetHost) || string.IsNullOrWhiteSpace(@namespace))
                    return null;

                var options = new TemporalClientConnectOptions
                {
                    TargetHost = targetHost,
                    Namespace = @namespace,
                    ApiKey = apiKey,
                    Tls = new TlsOptions()
                };

                var connectedClient = await connectAsync(options, cancellationToken);
                Volatile.Write(ref cachedClient, connectedClient);
                return connectedClient;
            }
            finally
            {
                initializationGate.Release();
            }
        }

        private static async Task<ITemporalClient> DefaultConnectAsync(
            TemporalClientConnectOptions options,
            CancellationToken _)
            => await TemporalClient.ConnectAsync(options);
    }
}

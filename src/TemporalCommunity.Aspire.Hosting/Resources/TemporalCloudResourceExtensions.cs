namespace TemporalCommunity.Aspire.Hosting;

/// <summary>
/// Extension methods for registering externally managed Temporal Cloud resources in Aspire.
/// </summary>
public static class TemporalCloudResourceExtensions
{
    /// <summary>
    /// Adds an externally managed Temporal Cloud namespace to the distributed application.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="address">The Temporal Cloud host:port address.</param>
    /// <param name="namespace">The Temporal Cloud namespace.</param>
    /// <param name="apiKey">Optional API key value to inject into dependent resources.</param>
    /// <param name="uiAddress">Optional Temporal Cloud UI address.</param>
    /// <returns>A builder for the Temporal Cloud resource.</returns>
    public static IResourceBuilder<TemporalCloudResource> AddTemporalCloud(
        this IDistributedApplicationBuilder builder,
        string name,
        string address,
        string @namespace,
        string? apiKey = null,
        string? uiAddress = null)
    {
        return builder.AddTemporalCloud(
            name,
            ReferenceExpression.Create($"{address}"),
            options =>
            {
                options.Namespace = @namespace;
                options.ApiKey = apiKey;
                options.UIAddress = uiAddress;
            });
    }

    /// <summary>
    /// Adds an externally managed Temporal Cloud namespace to the distributed application using parameter resources.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="address">A parameter containing the Temporal Cloud host:port address.</param>
    /// <param name="namespace">The Temporal Cloud namespace.</param>
    /// <param name="apiKey">Optional parameter containing the API key to inject into dependent resources.</param>
    /// <param name="uiAddress">Optional parameter containing the Temporal Cloud UI address.</param>
    /// <returns>A builder for the Temporal Cloud resource.</returns>
    public static IResourceBuilder<TemporalCloudResource> AddTemporalCloud(
        this IDistributedApplicationBuilder builder,
        string name,
        IResourceBuilder<ParameterResource> address,
        string @namespace,
        IResourceBuilder<ParameterResource>? apiKey = null,
        IResourceBuilder<ParameterResource>? uiAddress = null)
    {
        return builder.AddTemporalCloud(
            name,
            ReferenceExpression.Create($"{address.Resource}"),
            options =>
            {
                options.Namespace = @namespace;
                options.ApiKey = apiKey?.Resource;
                options.UIAddress = uiAddress?.Resource;
            });
    }

    /// <summary>
    /// Adds an externally managed Temporal Cloud namespace to the distributed application.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="address">The Temporal Cloud host:port address expression.</param>
    /// <param name="configure">Optional action to configure the cloud resource options.</param>
    /// <returns>A builder for the Temporal Cloud resource.</returns>
    public static IResourceBuilder<TemporalCloudResource> AddTemporalCloud(
        this IDistributedApplicationBuilder builder,
        string name,
        ReferenceExpression address,
        Action<TemporalCloudOptions>? configure = null)
    {
        var resource = new TemporalCloudResource(name, address);
        configure?.Invoke(resource.Options);

        return builder.AddResource(resource)
            .ExcludeFromManifest();
    }

    /// <summary>
    /// Adds a reference from a dependent service to a Temporal Cloud resource,
    /// automatically injecting connection environment variables.
    /// </summary>
    /// <typeparam name="TDestination">The type of the destination resource.</typeparam>
    /// <param name="builder">The resource builder for the dependent service.</param>
    /// <param name="source">The Temporal Cloud resource builder.</param>
    /// <returns>The updated resource builder.</returns>
    public static IResourceBuilder<TDestination> WithReference<TDestination>(
        this IResourceBuilder<TDestination> builder,
        IResourceBuilder<TemporalCloudResource> source)
        where TDestination : IResourceWithEnvironment
    {
        return builder.WithEnvironment(ctx =>
        {
            TemporalEnvironmentHelper.AddEnvironmentVariables(
                ctx,
                source.Resource.Options,
                source.Resource.ConnectionStringExpression);
        });
    }
}

namespace TemporalCommunity.Aspire.Hosting;

/// <summary>
/// Helper for injecting Temporal connection details as environment variables into dependent services.
/// </summary>
internal static class TemporalEnvironmentHelper
{
    /// <summary>
    /// Adds Temporal connection and configuration environment variables to a dependent service.
    /// This helper only maps the resource connection details and namespace into dependent services.
    /// It intentionally does not duplicate all resource-specific Temporal environment variables.
    /// </summary>
    /// <param name="ctx">The environment callback context for the dependent service.</param>
    /// <param name="options">The Temporal resource options containing namespace and codec configuration.</param>
    /// <param name="temporalAddress">The gRPC server address expression.</param>
    /// <param name="temporalUiAddress">The Web UI address expression.</param>
    internal static void AddEnvironmentVariables(
        EnvironmentCallbackContext ctx,
        TemporalResourceOptions options,
        object temporalAddress,
        object temporalUiAddress)
    {
        ctx.EnvironmentVariables["TEMPORAL_ADDRESS"] = temporalAddress;
        ctx.EnvironmentVariables["TEMPORAL_UI_ADDRESS"] = temporalUiAddress;
        ctx.EnvironmentVariables["TEMPORAL_NAMESPACE"] = options.Namespace;

        if (!string.IsNullOrEmpty(options.CodecAuth))
            ctx.EnvironmentVariables["TEMPORAL_CODEC_AUTH"] = options.CodecAuth;

        if (!string.IsNullOrEmpty(options.CodecEndpoint))
            ctx.EnvironmentVariables["TEMPORAL_CODEC_ENDPOINT"] = options.CodecEndpoint;
    }

    /// <summary>
    /// Adds Temporal Cloud connection and configuration environment variables to a dependent service.
    /// </summary>
    /// <param name="ctx">The environment callback context for the dependent service.</param>
    /// <param name="options">The Temporal Cloud options containing namespace, credentials, and codec configuration.</param>
    /// <param name="temporalAddress">The gRPC server address expression.</param>
    internal static void AddEnvironmentVariables(
        EnvironmentCallbackContext ctx,
        TemporalCloudOptions options,
        object temporalAddress)
    {
        ctx.EnvironmentVariables["TEMPORAL_ADDRESS"] = temporalAddress;
        ctx.EnvironmentVariables["TEMPORAL_NAMESPACE"] = options.Namespace;

        if (options.UIAddress is not null)
            ctx.EnvironmentVariables["TEMPORAL_UI_ADDRESS"] = options.UIAddress;

        if (options.ApiKey is not null)
            ctx.EnvironmentVariables["TEMPORAL_API_KEY"] = options.ApiKey;

        if (!string.IsNullOrEmpty(options.CodecAuth))
            ctx.EnvironmentVariables["TEMPORAL_CODEC_AUTH"] = options.CodecAuth;

        if (!string.IsNullOrEmpty(options.CodecEndpoint))
            ctx.EnvironmentVariables["TEMPORAL_CODEC_ENDPOINT"] = options.CodecEndpoint;
    }
}

using Temporalio.Testing;

namespace TemporalCommunity.Aspire.Hosting;

/// <summary>
/// Builds command-line arguments for Temporal dev server startup.
/// </summary>
internal static class TemporalArgsBuilder
{
    /// <summary>
    /// Builds the CLI arguments for a Temporal dev server.
    /// When <paramref name="fixedIpAndPort"/> is true the IP is always "0.0.0.0" and the port
    /// is always <see cref="TemporalResourceConstants.DefaultServiceEndpointPort"/> (container mode).
    /// When false, IP and port are taken from <paramref name="options"/> and --ui-port is also emitted (CLI mode).
    /// </summary>
    /// <param name="options">The resource options containing host, ports, and namespace configuration.</param>
    /// <param name="fixedIpAndPort">If true, uses fixed container defaults; if false, uses options values and emits --ui-port.</param>
    /// <param name="databaseFilename">An optional filename resolved from an Aspire data volume.</param>
    /// <returns>An array of CLI arguments for the temporal server start-dev command.</returns>
    internal static string[] BuildArgs(TemporalResourceOptions options, bool fixedIpAndPort = false, string? databaseFilename = null)
    {
        var args = new List<string> { "server", "start-dev" };

        if (fixedIpAndPort)
        {
            args.AddRange(["--ip", "0.0.0.0"]);
            args.AddRange(["--port", $"{TemporalResourceConstants.DefaultServiceEndpointPort}"]);
            args.AddRange(["--metrics-port", $"{TemporalResourceConstants.DefaultMetricsEndpointPort}"]);
        }
        else
        {
            args.AddRange(["--ip", options.Ip]);
            args.AddRange(["--port", options.Port.ToString()]);
            args.AddRange(["--metrics-port", options.MetricsPort.ToString()]);

            if (!options.IsHeadless)
                args.AddRange(["--ui-port", options.UIPort.ToString()]);
        }

        if (options.IsHeadless)
            args.Add("--headless");

        args.AddRange(["--log-level", options.DevServerOptions.LogLevel]);
        args.AddRange(["--log-format", options.DevServerOptions.LogFormat]);

        foreach (var ns in options.AdditionalNamespaces)
            args.AddRange(["--namespace", ns]);

        if (options.SearchAttributes != null)
        {
            foreach (var sa in options.SearchAttributes)
                args.AddRange(["--search-attribute", $"{sa.Name}={sa.ValueType}"]);
        }

        foreach (var dv in options.DynamicConfigValues)
            args.AddRange(["--dynamic-config-value", dv]);

        databaseFilename ??= options.DevServerOptions.DatabaseFilename;
        if (!string.IsNullOrEmpty(databaseFilename))
            args.AddRange(["--db-filename", databaseFilename]);

        return args.ToArray();
    }

    /// <summary>
    /// Creates SDK local-server options with the configured metrics endpoint.
    /// </summary>
    /// <remarks>
    /// <see cref="WorkflowEnvironmentStartLocalOptions"/> has no first-class metrics-port setting;
    /// the Temporal SDK exposes it through <see cref="DevServerOptions.ExtraArgs"/>.
    /// </remarks>
    internal static WorkflowEnvironmentStartLocalOptions BuildLocalOptions(TemporalResourceOptions options)
    {
        var localOptions = (WorkflowEnvironmentStartLocalOptions)options.Clone();
        var extraArgs = localOptions.DevServerOptions.ExtraArgs ?? [];

        if (extraArgs.Any(IsMetricsPortArgument))
        {
            throw new InvalidOperationException(
                "Configure TemporalResourceOptions.MetricsPort instead of passing --metrics-port in DevServerOptions.ExtraArgs.");
        }

        localOptions.DevServerOptions.ExtraArgs =
        [
            .. extraArgs,
            "--metrics-port",
            options.MetricsPort.ToString(System.Globalization.CultureInfo.InvariantCulture)
        ];

        return localOptions;

        static bool IsMetricsPortArgument(string argument) =>
            argument.Equals("--metrics-port", StringComparison.Ordinal) ||
            argument.StartsWith("--metrics-port=", StringComparison.Ordinal);
    }
}

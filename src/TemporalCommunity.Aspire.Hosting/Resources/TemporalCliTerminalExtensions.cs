using Aspire.Hosting.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

#pragma warning disable ASPIRETERMINAL001 // Isolated use of Aspire's experimental terminal API.
#pragma warning disable ASPIRECONTAINERRUNTIME001 // Resolve the runtime selected by Aspire for container exec.

namespace TemporalCommunity.Aspire.Hosting;

/// <summary>Opens resource-configured Temporal CLI shells in the Aspire Dashboard.</summary>
public static class TemporalCliTerminalExtensions
{
    internal const string CommandName = "temporal-cli";

    /// <summary>Adds a Dashboard terminal using the host Temporal CLI for the SDK-managed server.</summary>
    /// <param name="builder">The SDK-managed Temporal server.</param>
    /// <returns>The resource builder.</returns>
    public static IResourceBuilder<TemporalLocalResource> WithCliTerminal(
        this IResourceBuilder<TemporalLocalResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return WithCliTerminalCore(builder, (_, _) =>
        {
            TemporalCliLocator.EnsureAvailable();
            var address = builder.Resource.WorkflowEnvironment?.Client.Connection.Options.TargetHost
                ?? throw new InvalidOperationException("The Temporal local server address is not available yet.");
            return Task.FromResult(CreateHostOptions(builder.Resource.Name, address,
                builder.Resource.Options.Namespace, OperatingSystem.IsWindows()));
        });
    }

    /// <summary>Adds a Dashboard terminal using the host Temporal CLI for the CLI server.</summary>
    /// <param name="builder">The Temporal CLI server.</param>
    /// <returns>The resource builder.</returns>
    public static IResourceBuilder<TemporalCliServerResource> WithCliTerminal(
        this IResourceBuilder<TemporalCliServerResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return WithCliTerminalCore(builder, async (context, _) =>
        {
            TemporalCliLocator.EnsureAvailable();
            var address = await builder.Resource.ConnectionStringExpression.GetValueAsync(context.CancellationToken)
                ?? throw new InvalidOperationException("The Temporal CLI server address is not available yet.");
            return CreateHostOptions(builder.Resource.Name, address,
                builder.Resource.Options.Namespace, OperatingSystem.IsWindows());
        });
    }

    /// <summary>Adds a Dashboard terminal using the Temporal CLI bundled in the running container.</summary>
    /// <param name="builder">The Temporal container server.</param>
    /// <returns>The resource builder.</returns>
    public static IResourceBuilder<TemporalContainerResource> WithCliTerminal(
        this IResourceBuilder<TemporalContainerResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return WithCliTerminalCore(builder, async (context, snapshot) =>
        {
            var runtime = await context.Services.GetRequiredService<IContainerRuntimeResolver>()
                .ResolveAsync(context.CancellationToken);
#pragma warning disable CA1308 // Docker and Podman executable names are lowercase on Unix.
            return CreateContainerOptions(builder.Resource.Name, runtime.Name.ToLowerInvariant(),
                GetContainerId(snapshot)!, builder.Resource.Options.Namespace);
#pragma warning restore CA1308
        });
    }

    internal static TerminalLaunchOptions CreateHostOptions(
        string resourceName, string address, string temporalNamespace, bool isWindows)
    {
        var options = new TerminalLaunchOptions
        {
            Title = $"Temporal CLI — {resourceName}",
            Executable = isWindows ? "powershell.exe" : "/bin/sh",
            Arguments = isWindows ? ["-NoLogo", "-NoExit"] : ["-i"],
            Placement = TerminalPlacement.Dock
        };
        options.EnvironmentVariables["TEMPORAL_ADDRESS"] = address;
        options.EnvironmentVariables["TEMPORAL_NAMESPACE"] = temporalNamespace;
        // Local dev servers do not use Cloud authentication inherited from the host shell.
        options.EnvironmentVariables["TEMPORAL_TLS"] = "false";
        options.EnvironmentVariables["TEMPORAL_API_KEY"] = "";
        return options;
    }

    internal static TerminalLaunchOptions CreateContainerOptions(
        string resourceName, string runtime, string containerId, string temporalNamespace)
    {
        var shell = CreateHostOptions(resourceName,
            $"localhost:{TemporalResourceConstants.DefaultServiceEndpointPort}", temporalNamespace, isWindows: false);
        var options = new TerminalLaunchOptions
        {
            Title = shell.Title,
            Executable = runtime,
            Arguments = ["exec", "-it"],
            Placement = TerminalPlacement.Dock
        };
        foreach (var (name, value) in shell.EnvironmentVariables)
        {
            options.Arguments.Add("--env");
            options.Arguments.Add(name);
            options.EnvironmentVariables[name] = value;
        }
        options.Arguments.Add(containerId);
        options.Arguments.Add(shell.Executable);
        foreach (var argument in shell.Arguments)
            options.Arguments.Add(argument);
        return options;
    }

    private static IResourceBuilder<T> WithCliTerminalCore<T>(
        IResourceBuilder<T> builder,
        Func<ExecuteCommandContext, CustomResourceSnapshot, Task<TerminalLaunchOptions>> createOptions)
        where T : IResource
    {
        if (!builder.ApplicationBuilder.ExecutionContext.IsRunMode)
            return builder;

        return builder.WithCommand(CommandName, "Open Temporal CLI", async context =>
        {
            var notifications = context.Services.GetRequiredService<ResourceNotificationService>();
            if (!notifications.TryGetCurrentState(context.ResourceName, out var resourceEvent) ||
                !CanOpen(builder.Resource, resourceEvent.Snapshot))
                return CommandResults.Failure("The Temporal server is not running and healthy, or its container ID is not available yet.");

            AspireTerminal? terminal = null;
            try
            {
                var options = await createOptions(context, resourceEvent.Snapshot);
                context.CancellationToken.ThrowIfCancellationRequested();
                terminal = context.Services.GetRequiredService<TerminalService>().CreateTerminal(options);
                terminal.Start();
                terminal.Show();
                return CommandResults.Success();
            }
            catch (Exception exception)
            {
                if (terminal is not null)
                    await terminal.DisposeAsync();
                if (exception is OperationCanceledException)
                    throw;
                return CommandResults.Failure(exception.Message);
            }
        }, commandOptions: new CommandOptions
        {
            Description = "Open a shell configured to run Temporal CLI commands against this local server.",
            IconName = "WindowConsole",
            UpdateState = context => CanOpen(builder.Resource, context.ResourceSnapshot)
                ? ResourceCommandState.Enabled
                : ResourceCommandState.Disabled
        });
    }

    private static bool CanOpen(IResource resource, CustomResourceSnapshot snapshot) =>
        snapshot.State?.Text == KnownResourceStates.Running &&
        snapshot.HealthStatus == HealthStatus.Healthy &&
        (resource is not ContainerResource || !string.IsNullOrEmpty(GetContainerId(snapshot)));

    private static string? GetContainerId(CustomResourceSnapshot snapshot) =>
        snapshot.Properties.FirstOrDefault(property => property.Name == "container.id")?.Value as string;
}

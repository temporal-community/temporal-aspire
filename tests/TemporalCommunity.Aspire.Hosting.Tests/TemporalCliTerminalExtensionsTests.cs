using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable ASPIRETERMINAL001 // Validate the isolated terminal integration.

namespace TemporalCommunity.Aspire.Hosting.Tests;

public class TemporalCliTerminalExtensionsTests
{
    private static readonly string[] UnixShellArguments = ["-i"];
    private static readonly string[] WindowsShellArguments = ["-NoLogo", "-NoExit"];
    private static readonly string[] ContainerArguments =
    [
        "exec", "-it", "--env", "TEMPORAL_ADDRESS", "--env", "TEMPORAL_NAMESPACE",
        "--env", "TEMPORAL_TLS", "--env", "TEMPORAL_API_KEY", "current-container-id", "/bin/sh", "-i"
    ];

    [Theory]
    [InlineData("local")]
    [InlineData("cli")]
    [InlineData("container")]
    public void WithCliTerminal_RegistersCommandForEachLocalMode(string mode)
    {
        var resource = Register(DistributedApplication.CreateBuilder([]), mode);

        var command = Assert.Single(resource.Annotations.OfType<ResourceCommandAnnotation>(),
            annotation => annotation.Name == TemporalCliTerminalExtensions.CommandName);
        Assert.Equal("Open Temporal CLI", command.DisplayName);
        Assert.Equal("WindowConsole", command.IconName);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("cli")]
    [InlineData("container")]
    public void WithCliTerminal_DoesNotRegisterCommandWhenPublishing(string mode)
    {
        var resource = Register(DistributedApplication.CreateBuilder(["--publisher", "manifest"]), mode);

        Assert.DoesNotContain(resource.Annotations.OfType<ResourceCommandAnnotation>(),
            command => command.Name == TemporalCliTerminalExtensions.CommandName);
    }

    [Theory]
    [InlineData("local", "Running", null, ResourceCommandState.Enabled)]
    [InlineData("cli", "Running", null, ResourceCommandState.Enabled)]
    [InlineData("container", "Running", "current-id", ResourceCommandState.Enabled)]
    [InlineData("container", "Running", null, ResourceCommandState.Disabled)]
    [InlineData("local", "Exited", null, ResourceCommandState.Disabled)]
    [InlineData("cli", "Exited", null, ResourceCommandState.Disabled)]
    [InlineData("container", "Exited", "old-id", ResourceCommandState.Disabled)]
    [InlineData("local", "Starting", null, ResourceCommandState.Disabled)]
    [InlineData("cli", "FailedToStart", null, ResourceCommandState.Disabled)]
    [InlineData("container", null, "old-id", ResourceCommandState.Disabled)]
    public void WithCliTerminal_EnablesOnlyRunningServersWithUsableContainerId(
        string mode, string? state, string? containerId, ResourceCommandState expected)
    {
        var resource = Register(DistributedApplication.CreateBuilder([]), mode);
        var command = resource.Annotations.OfType<ResourceCommandAnnotation>()
            .Single(annotation => annotation.Name == TemporalCliTerminalExtensions.CommandName);
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new UpdateCommandStateContext
        {
            Services = services,
            ResourceSnapshot = new CustomResourceSnapshot
            {
                ResourceType = "temporal",
                State = state is null ? null : new ResourceStateSnapshot(state, null),
                Properties = containerId is null ? [] : [new("container.id", containerId)]
            }.WithHealthReports([new("temporal_check", HealthStatus.Healthy, null, null)])
        };

        Assert.Equal(expected, command.UpdateState(context));
    }

    [Theory]
    [InlineData("local")]
    [InlineData("cli")]
    [InlineData("container")]
    public void WithCliTerminal_WaitsForHealthyServerAndTracksHealthChanges(string mode)
    {
        var resource = Register(DistributedApplication.CreateBuilder([]), mode);
        var command = resource.Annotations.OfType<ResourceCommandAnnotation>()
            .Single(annotation => annotation.Name == TemporalCliTerminalExtensions.CommandName);
        using var services = new ServiceCollection().BuildServiceProvider();
        var snapshot = new CustomResourceSnapshot
        {
            ResourceType = "temporal",
            State = new ResourceStateSnapshot(KnownResourceStates.Running, null),
            Properties = [new("container.id", "current-container-id")]
        };
        foreach (var health in new HealthStatus?[]
            { null, HealthStatus.Unhealthy, HealthStatus.Degraded, HealthStatus.Healthy, HealthStatus.Unhealthy, HealthStatus.Healthy })
        {
            var state = command.UpdateState(new UpdateCommandStateContext
            {
                Services = services,
                ResourceSnapshot = snapshot.WithHealthReports([new("temporal_check", health, null, null)])
            });
            Assert.Equal(health == HealthStatus.Healthy ? ResourceCommandState.Enabled : ResourceCommandState.Disabled, state);
        }
    }

    [Theory]
    [InlineData("local", "Exited")]
    [InlineData("cli", "Exited")]
    [InlineData("container", "Exited")]
    [InlineData("container", "Running")]
    public async Task WithCliTerminal_RechecksCurrentStateBeforeLaunching(string mode, string currentState)
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var resource = Register(builder, mode);
        var command = resource.Annotations.OfType<ResourceCommandAnnotation>()
            .Single(annotation => annotation.Name == TemporalCliTerminalExtensions.CommandName);
        await using var app = builder.Build();
        var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
        await notifications.PublishUpdateAsync(resource, snapshot => (snapshot with
        {
            State = new ResourceStateSnapshot(KnownResourceStates.Running, null),
            Properties = [new("container.id", "previous-container-id")]
        }).WithHealthReports([new("temporal_check", HealthStatus.Healthy, null, null)]));
        Assert.True(notifications.TryGetCurrentState(resource.Name, out var initial));
        Assert.Equal(ResourceCommandState.Enabled, command.UpdateState(new UpdateCommandStateContext
        {
            Services = app.Services,
            ResourceSnapshot = initial.Snapshot
        }));

        await notifications.PublishUpdateAsync(resource, snapshot => snapshot with
        {
            State = new ResourceStateSnapshot(currentState, null),
            Properties = []
        });
        var result = await command.ExecuteCommand(new ExecuteCommandContext
        {
            Services = app.Services,
            ResourceName = resource.Name,
            CancellationToken = CancellationToken.None,
            Logger = NullLogger.Instance,
            Arguments = new InteractionInputCollection([])
        });

        Assert.False(result.Success);
        Assert.Equal("The Temporal server is not running and healthy, or its container ID is not available yet.", result.Message);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("cli")]
    [InlineData("container")]
    public async Task WithCliTerminal_RechecksHealthBeforeLaunching(string mode)
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var resource = Register(builder, mode);
        var command = resource.Annotations.OfType<ResourceCommandAnnotation>()
            .Single(annotation => annotation.Name == TemporalCliTerminalExtensions.CommandName);
        await using var app = builder.Build();
        var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
        await notifications.PublishUpdateAsync(resource, snapshot => (snapshot with
        {
            State = new ResourceStateSnapshot(KnownResourceStates.Running, null),
            Properties = [new("container.id", "current-container-id")]
        }).WithHealthReports([new("temporal_check", HealthStatus.Healthy, null, null)]));
        Assert.True(notifications.TryGetCurrentState(resource.Name, out var initial));
        Assert.Equal(ResourceCommandState.Enabled, command.UpdateState(new UpdateCommandStateContext
        {
            Services = app.Services,
            ResourceSnapshot = initial.Snapshot
        }));

        foreach (var health in new HealthStatus?[] { null, HealthStatus.Unhealthy, HealthStatus.Degraded })
        {
            await notifications.PublishUpdateAsync(resource, snapshot =>
                snapshot.WithHealthReports([new("temporal_check", health, null, null)]));
            var result = await command.ExecuteCommand(new ExecuteCommandContext
            {
                Services = app.Services,
                ResourceName = resource.Name,
                CancellationToken = CancellationToken.None,
                Logger = NullLogger.Instance,
                Arguments = new InteractionInputCollection([])
            });
            Assert.False(result.Success);
            Assert.Equal("The Temporal server is not running and healthy, or its container ID is not available yet.", result.Message);
        }
    }

    [Theory]
    [InlineData(false, "/bin/sh")]
    [InlineData(true, "powershell.exe")]
    public void HostTerminal_UsesPlatformShellAndExplicitLocalConnectionSettings(bool windows, string executable)
    {
        var options = TemporalCliTerminalExtensions.CreateHostOptions("orders", "localhost:17233", "orders-dev", windows);

        Assert.Equal(executable, options.Executable);
        Assert.Equal(windows ? WindowsShellArguments : UnixShellArguments, options.Arguments);
        Assert.Equal(TerminalPlacement.Dock, options.Placement);
        Assert.Equal("localhost:17233", options.EnvironmentVariables["TEMPORAL_ADDRESS"]);
        Assert.Equal("orders-dev", options.EnvironmentVariables["TEMPORAL_NAMESPACE"]);
        Assert.Equal("false", options.EnvironmentVariables["TEMPORAL_TLS"]);
        Assert.Equal("", options.EnvironmentVariables["TEMPORAL_API_KEY"]);
        Assert.DoesNotContain("orders-dev", options.Arguments);
    }

    [Theory]
    [InlineData("docker")]
    [InlineData("podman")]
    public void ContainerTerminal_UsesRuntimeExecAndInternalAddress(string runtime)
    {
        var options = TemporalCliTerminalExtensions.CreateContainerOptions("orders", runtime, "current-container-id", "orders-dev");

        Assert.Equal(runtime, options.Executable);
        Assert.Equal(ContainerArguments, options.Arguments);
        Assert.Equal("localhost:7233", options.EnvironmentVariables["TEMPORAL_ADDRESS"]);
        Assert.Equal("orders-dev", options.EnvironmentVariables["TEMPORAL_NAMESPACE"]);
        Assert.DoesNotContain("orders-dev", options.Arguments);
        Assert.DoesNotContain("localhost:7233", options.Arguments);
    }

    private static IResource Register(IDistributedApplicationBuilder builder, string mode) => mode switch
    {
        "local" => builder.AddTemporalLocalDevServer("temporal").WithCliTerminal().Resource,
        "cli" => builder.AddTemporalCliServer("temporal", null, () => true).WithCliTerminal().Resource,
        "container" => builder.AddTemporalDevContainer("temporal").WithCliTerminal().Resource,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}

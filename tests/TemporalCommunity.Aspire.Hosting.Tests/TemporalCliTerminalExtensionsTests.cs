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
    [InlineData("cloud")]
    public void WithCliTerminal_RegistersCommandForEachResourceType(string mode)
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
    [InlineData("cloud")]
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
    [InlineData("cloud", "Running", null, ResourceCommandState.Enabled)]
    [InlineData("cloud", "Exited", null, ResourceCommandState.Disabled)]
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
    [InlineData("cloud")]
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
    [InlineData("cloud", "Exited")]
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
    [InlineData("cloud")]
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
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CloudTerminal_UsesResourceCredentialsWithTlsAndNoSecretsInArguments(bool windows, bool parameterized)
    {
        const string apiKey = "test-key-$value;with-spaces only-for-tests";
        var builder = DistributedApplication.CreateBuilder([]);
        var resource = parameterized
            ? builder.AddTemporalCloud("orders", builder.AddParameter("address", "orders.prod.tmprl.cloud:7233"),
                builder.AddParameter("namespace", "orders.prod"), builder.AddParameter("api-key", apiKey, secret: true)).Resource
            : builder.AddTemporalCloud("orders", "orders.prod.tmprl.cloud:7233", "orders.prod",
                options => options.ApiKey = apiKey).Resource;

        var options = await TemporalCliTerminalExtensions.CreateCloudOptionsAsync(resource, windows, CancellationToken.None);

        Assert.Equal(windows ? "powershell.exe" : "/bin/sh", options.Executable);
        Assert.Equal(windows ? WindowsShellArguments : UnixShellArguments, options.Arguments);
        Assert.Equal(TerminalPlacement.Dock, options.Placement);
        Assert.Equal("orders.prod.tmprl.cloud:7233", options.EnvironmentVariables["TEMPORAL_ADDRESS"]);
        Assert.Equal("orders.prod", options.EnvironmentVariables["TEMPORAL_NAMESPACE"]);
        Assert.Equal("true", options.EnvironmentVariables["TEMPORAL_TLS"]);
        Assert.Equal(apiKey, options.EnvironmentVariables["TEMPORAL_API_KEY"]);
        Assert.DoesNotContain(apiKey, string.Join(" ", options.Arguments), StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, options.Title, StringComparison.Ordinal);

        // Resolve current options for each new terminal, rather than caching credentials at registration.
        resource.Options.ApiKey = "replacement-test-key";
        var next = await TemporalCliTerminalExtensions.CreateCloudOptionsAsync(resource, windows, CancellationToken.None);
        Assert.Equal("replacement-test-key", next.EnvironmentVariables["TEMPORAL_API_KEY"]);
        Assert.Equal(apiKey, options.EnvironmentVariables["TEMPORAL_API_KEY"]);
    }

    [Theory]
    [InlineData("", "orders.prod", "test-key", "address")]
    [InlineData("orders.prod.tmprl.cloud:7233", " ", "test-key", "namespace")]
    [InlineData("orders.prod.tmprl.cloud:7233", "orders.prod", null, "API key")]
    public async Task CloudTerminal_RejectsMissingConnectionSettings(
        string address, string temporalNamespace, string? apiKey, string missing)
    {
        var resource = DistributedApplication.CreateBuilder([])
            .AddTemporalCloud("orders", address, temporalNamespace, options => options.ApiKey = apiKey).Resource;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TemporalCliTerminalExtensions.CreateCloudOptionsAsync(resource, false, CancellationToken.None));

        Assert.Equal($"The Temporal Cloud {missing} is required to open a CLI terminal.", error.Message);
    }

    [Fact]
    public void CloudTerminal_WithoutHealthCheckIsEnabledForRunningResource()
    {
        var resource = DistributedApplication.CreateBuilder([])
            .AddTemporalCloud("orders", "orders.prod.tmprl.cloud:7233", "orders.prod", options => options.ApiKey = "test-key")
            .WithCliTerminal().Resource;
        var snapshot = Assert.Single(resource.Annotations.OfType<ResourceSnapshotAnnotation>()).InitialSnapshot;
        var command = Assert.Single(resource.Annotations.OfType<ResourceCommandAnnotation>());
        using var services = new ServiceCollection().BuildServiceProvider();

        Assert.Equal(ResourceCommandState.Enabled, command.UpdateState(new UpdateCommandStateContext
        {
            Services = services,
            ResourceSnapshot = snapshot
        }));
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
        "cloud" => builder.AddTemporalCloud("temporal", builder.AddParameter("address"),
            builder.AddParameter("namespace"), builder.AddParameter("api-key", secret: true)).WithCliTerminal().Resource,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}

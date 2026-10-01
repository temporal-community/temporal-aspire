using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Temporalio.Testing;
using Xunit;

#pragma warning disable ASPIRETERMINAL001 // Exercise real PTY sessions in opt-in integration tests.

namespace TemporalCommunity.Aspire.Hosting.Tests;

public partial class TemporalDevServerIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DataVolume_PreservesWorkflowAndTerminalConnectionAcrossServerRestart(bool container)
    {
        if (!IntegrationTestsEnabled())
            return;
        if (container)
            await AssertDockerAvailableAsync().ConfigureAwait(true);

        var directory = Directory.CreateTempSubdirectory("temporal-persistence-");
        var name = $"temporal-persistence-{Guid.NewGuid():N}";
        var options = CreateOptions();
        options.Namespace = "orders-dev";
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = [],
            DisableDashboard = true
        });
        builder.Configuration["Aspire:Store:Path"] = directory.FullName;
        IResource resource;
        if (container)
        {
            resource = builder.AddTemporalDevContainer("temporal", configuration =>
            {
                configuration.Namespace = options.Namespace;
            }).WithDataVolume(name).WithCliTerminal().Resource;
        }
        else
        {
            resource = builder.AddTemporalCliServer("temporal", configuration =>
            {
                configuration.TargetHost = options.TargetHost;
                configuration.UIPort = options.UIPort;
                configuration.MetricsPort = options.MetricsPort;
                configuration.Namespace = options.Namespace;
            }).WithDataVolume(name).WithCliTerminal().Resource;
        }

        await using var app = builder.Build();
        var executionContext = app.Services.GetRequiredService<DistributedApplicationExecutionContext>();
        var configurationResult = await ExecutionConfigurationBuilder.Create(resource)
            .WithArgumentsConfig().WithEnvironmentVariablesConfig()
            .BuildAsync(executionContext, NullLogger.Instance).ConfigureAwait(true);
        Assert.Null(configurationResult.Exception);
        var serverArguments = configurationResult.Arguments.Select(argument => argument.Value).ToArray();
        var workflowId = $"persisted-{Guid.NewGuid():N}";

        try
        {
            for (var run = 0; run < 2; run++)
            {
                var arguments = container
                    ? new List<string>
                    {
                        "run", "--rm", "--name", name, "--volume", $"{name}:/home/temporal",
                        "-p", $"{options.Port}:7233",
                        $"docker.io/{TemporalResourceConstants.TemporalImage}:{TemporalResourceConstants.DefaultTag}"
                    }
                    : [];
                arguments.AddRange(serverArguments);
                using var process = StartProcess(container ? "docker" : "temporal", arguments);
                try
                {
                    await WaitForTemporalAsync(options.Port).ConfigureAwait(true);
                    var connectionArguments = new[]
                    {
                        "--address", $"localhost:{options.Port}", "--namespace", options.Namespace, "--output", "json"
                    };
                    if (run == 0)
                    {
                        await RunProcessAsync("temporal", [.. connectionArguments, "workflow", "start",
                            "--type", "PersistenceProbe", "--task-queue", "unpolled-test-queue", "--workflow-id", workflowId])
                            .ConfigureAwait(true);
                    }
                    var description = await RunProcessAsync("temporal",
                        [.. connectionArguments, "workflow", "describe", "--workflow-id", workflowId]).ConfigureAwait(true);
                    Assert.Contains(workflowId, description.StandardOutput, StringComparison.Ordinal);

                    var launchOptions = container
                        ? TemporalCliTerminalExtensions.CreateContainerOptions("temporal", "docker", name, options.Namespace)
                        : TemporalCliTerminalExtensions.CreateHostOptions("temporal", $"localhost:{options.Port}",
                            options.Namespace, OperatingSystem.IsWindows());
                    await AssertTerminalConnectionAsync(app.Services, launchOptions, workflowId).ConfigureAwait(true);
                }
                finally
                {
                    if (container)
                        await RunProcessAsync("docker", ["stop", name], assertSuccess: false).ConfigureAwait(true);
                    await StopProcessAsync(process).ConfigureAwait(true);
                }
            }
        }
        finally
        {
            if (container)
                await RunProcessAsync("docker", ["volume", "rm", name], assertSuccess: false).ConfigureAwait(true);
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SdkLocalTerminal_ConnectsToLiveServerAfterRestart()
    {
        if (!IntegrationTestsEnabled())
            return;

        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        var options = CreateOptions();
        options.Namespace = "sdk-terminal-test";
        var resource = builder.AddTemporalLocalDevServer("temporal").WithCliTerminal().Resource;
        await using var app = builder.Build();
        for (var run = 0; run < 2; run++)
        {
            var environment = await WorkflowEnvironment.StartLocalAsync(
                TemporalArgsBuilder.BuildLocalOptions(options)).ConfigureAwait(true);
            await using var environmentLifetime = environment.ConfigureAwait(true);
            resource.WorkflowEnvironment = environment;
            var address = environment.Client.Connection.Options.TargetHost!;
            var launchOptions = TemporalCliTerminalExtensions.CreateHostOptions(resource.Name, address,
                options.Namespace, OperatingSystem.IsWindows());

            await AssertTerminalConnectionAsync(app.Services, launchOptions).ConfigureAwait(true);
            resource.WorkflowEnvironment = null;
        }
    }

    private static async Task AssertTerminalConnectionAsync(
        IServiceProvider services, TerminalLaunchOptions options, string? workflowId = null)
    {
        options.Columns = 160;
        options.Rows = 60;
        var terminal = services.GetRequiredService<TerminalService>().CreateTerminal(options);
        await using var terminalLifetime = terminal.ConfigureAwait(true);
        terminal.Start();
        // The completion marker is formatted at runtime, so echoed input cannot satisfy the assertion.
        var command = OperatingSystem.IsWindows() && options.Executable == "powershell.exe"
            ? "temporal workflow list --output json; Write-Output ('CLI_EXIT_' + $LASTEXITCODE)\r"
            : "temporal workflow list --output json; printf '\\nCLI_EXIT_%s\\n' \"$?\"\r";
        await terminal.SendTextAsync(command).ConfigureAwait(true);
        await terminal.WaitForTextAsync("CLI_EXIT_0", TimeSpan.FromSeconds(30)).ConfigureAwait(true);
        if (workflowId is not null)
            Assert.Contains(workflowId, terminal.GetScreenText(), StringComparison.Ordinal);
    }
}

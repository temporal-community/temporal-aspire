using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using TemporalCommunity.Aspire.Hosting;
using Temporalio.Api.WorkflowService.V1;
using Temporalio.Client;
using Xunit;

namespace TemporalCommunity.Aspire.Hosting.Tests;

public partial class TemporalDevServerIntegrationTests
{
    private const string IntegrationTestEnvironmentVariable = "RUN_TEMPORAL_INTEGRATION_TESTS";
    private static readonly SemaphoreSlim ManifestLock = new(1, 1);

    [Fact]
    public async Task CliServer_StartsWithReachableGrpcUiAndMetricsEndpoints()
    {
        if (!IntegrationTestsEnabled())
            return;

        var options = CreateOptions();
        using var process = StartProcess("temporal", TemporalArgsBuilder.BuildArgs(options));

        try
        {
            await WaitForTemporalAsync(options.Port).ConfigureAwait(true);
            await WaitForSuccessAsync(new Uri($"http://127.0.0.1:{options.UIPort}")).ConfigureAwait(true);
            await WaitForSuccessAsync(new Uri($"http://127.0.0.1:{options.MetricsPort}/metrics")).ConfigureAwait(true);
        }
        finally
        {
            await StopProcessAsync(process).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task ContainerServer_StartsWithReachableGrpcUiAndMetricsEndpoints()
    {
        if (!IntegrationTestsEnabled())
            return;

        await AssertDockerAvailableAsync().ConfigureAwait(true);

        var servicePort = GetAvailablePort();
        var uiPort = GetAvailablePort();
        var metricsPort = GetAvailablePort();
        var containerName = $"temporal-aspire-test-{Guid.NewGuid():N}";
        var options = new TemporalResourceOptions
        {
            TargetHost = $"127.0.0.1:{servicePort}",
            UIPort = uiPort,
            MetricsPort = metricsPort
        };
        var dockerArgs = new List<string>
        {
            "run",
            "--rm",
            "--name",
            containerName,
            "-p",
            $"{servicePort}:{TemporalResourceConstants.DefaultServiceEndpointPort}",
            "-p",
            $"{uiPort}:{TemporalResourceConstants.DefaultUIEndpointPort}",
            "-p",
            $"{metricsPort}:{TemporalResourceConstants.DefaultMetricsEndpointPort}",
            $"docker.io/{TemporalResourceConstants.TemporalImage}:{TemporalResourceConstants.DefaultTag}"
        };
        dockerArgs.AddRange(TemporalArgsBuilder.BuildArgs(options, fixedIpAndPort: true));

        using var process = StartProcess("docker", dockerArgs);

        try
        {
            await WaitForTemporalAsync(servicePort).ConfigureAwait(true);
            await WaitForSuccessAsync(new Uri($"http://127.0.0.1:{uiPort}")).ConfigureAwait(true);
            await WaitForSuccessAsync(new Uri($"http://127.0.0.1:{metricsPort}/metrics")).ConfigureAwait(true);
        }
        finally
        {
            await RunProcessAsync(
                "docker",
                ["stop", containerName],
                assertSuccess: false).ConfigureAwait(true);
            await StopProcessAsync(process).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task ContainerResource_DefaultPortCollisionUsesAllocatedPortsAndInjectsDependentEnvironment()
    {
        if (!IntegrationTestsEnabled())
            return;

        await AssertDockerAvailableAsync().ConfigureAwait(true);

        var blockerName = $"temporal-aspire-port-blocker-{Guid.NewGuid():N}";
        Process? blockerProcess = null;
        var appHostDirectory = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "TemporalCommunity.Aspire.Hosting.TestAppHost");

        try
        {
            if (DefaultPortsAreAvailable())
            {
                blockerProcess = StartProcess(
                    "docker",
                    [
                        "run",
                        "--rm",
                        "--name",
                        blockerName,
                        "-p",
                        $"{TemporalResourceConstants.DefaultServiceEndpointPort}:{TemporalResourceConstants.DefaultServiceEndpointPort}",
                        "-p",
                        $"{TemporalResourceConstants.DefaultUIEndpointPort}:{TemporalResourceConstants.DefaultUIEndpointPort}",
                        "-p",
                        $"{TemporalResourceConstants.DefaultMetricsEndpointPort}:{TemporalResourceConstants.DefaultMetricsEndpointPort}",
                        $"docker.io/{TemporalResourceConstants.TemporalImage}:{TemporalResourceConstants.DefaultTag}",
                        "server",
                        "start-dev",
                        "--ip",
                        "0.0.0.0",
                        "--port",
                        TemporalResourceConstants.DefaultServiceEndpointPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "--metrics-port",
                        TemporalResourceConstants.DefaultMetricsEndpointPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "--namespace",
                        "default"
                    ]);

                await WaitForTemporalAsync(TemporalResourceConstants.DefaultServiceEndpointPort).ConfigureAwait(true);
            }

            await RunProcessAsync(
                "aspire",
                ["start", "--isolated", "--format", "Json", "--non-interactive", "--nologo"],
                workingDirectory: appHostDirectory).ConfigureAwait(true);
            await RunProcessAsync(
                "aspire",
                ["wait", "temporal", "--non-interactive"],
                workingDirectory: appHostDirectory).ConfigureAwait(true);
            await RunProcessAsync(
                "aspire",
                ["wait", "dependent", "--non-interactive"],
                workingDirectory: appHostDirectory).ConfigureAwait(true);

            var describeResult = await RunProcessAsync(
                "aspire",
                ["describe", "--format", "Json", "--non-interactive"],
                workingDirectory: appHostDirectory).ConfigureAwait(true);
            var resources = ParseDescribeResources(describeResult.StandardOutput);
            var temporal = resources.Single(ResourceHasDisplayName("temporal"))!;
            var dependent = resources.Single(ResourceHasDisplayName("dependent"))!;

            Assert.Equal("Healthy", temporal["healthStatus"]?.GetValue<string>());

            var servicePort = GetResourcePort(temporal, TemporalResourceConstants.ServiceEndpointName);
            var uiPort = GetResourcePort(temporal, TemporalResourceConstants.UIEndpointName);
            var metricsPort = GetResourcePort(temporal, TemporalResourceConstants.MetricsEndpointName);

            Assert.NotEqual(TemporalResourceConstants.DefaultServiceEndpointPort, servicePort);
            Assert.NotEqual(TemporalResourceConstants.DefaultUIEndpointPort, uiPort);
            Assert.NotEqual(TemporalResourceConstants.DefaultMetricsEndpointPort, metricsPort);

            var containerId = temporal["properties"]?["container.id"]?.GetValue<string>();
            Assert.False(string.IsNullOrWhiteSpace(containerId));
            await AssertContainerPortMappingAsync(
                containerId,
                TemporalResourceConstants.DefaultServiceEndpointPort,
                servicePort).ConfigureAwait(true);
            await AssertContainerPortMappingAsync(
                containerId,
                TemporalResourceConstants.DefaultUIEndpointPort,
                uiPort).ConfigureAwait(true);
            await AssertContainerPortMappingAsync(
                containerId,
                TemporalResourceConstants.DefaultMetricsEndpointPort,
                metricsPort).ConfigureAwait(true);

            var environment = dependent["environment"]?.AsObject();
            Assert.NotNull(environment);
            Assert.Equal($"localhost:{servicePort}", environment["TEMPORAL_ADDRESS"]?.GetValue<string>());
            Assert.Equal($"http://localhost:{uiPort}", environment["TEMPORAL_UI_ADDRESS"]?.GetValue<string>());

            await WaitForTemporalAsync(servicePort).ConfigureAwait(true);
            await WaitForSuccessAsync(new Uri($"http://127.0.0.1:{uiPort}")).ConfigureAwait(true);
            await WaitForSuccessAsync(new Uri($"http://127.0.0.1:{metricsPort}/metrics")).ConfigureAwait(true);
        }
        finally
        {
            await RunProcessAsync(
                "aspire",
                ["stop", "--non-interactive"],
                workingDirectory: appHostDirectory,
                assertSuccess: false).ConfigureAwait(true);

            if (blockerProcess is not null)
            {
                await RunProcessAsync(
                    "docker",
                    ["stop", blockerName],
                    assertSuccess: false).ConfigureAwait(true);
                await blockerProcess.WaitForExitAsync().ConfigureAwait(true);
                blockerProcess.Dispose();
            }
        }
    }

    [Fact]
    public async Task PublishManifest_ExcludesDevelopmentTemporalResource()
    {
        if (!IntegrationTestsEnabled())
            return;

        var manifest = await PublishManifestAsync("SampleAppHost").ConfigureAwait(true);

        Assert.DoesNotContain("\"temporal\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishManifest_CloudSampleWiresParametersAndExcludesCloudResource()
    {
        if (!IntegrationTestsEnabled())
            return;

        var manifest = await PublishManifestAsync(
            "SampleCloudAppHost",
            new Dictionary<string, string>
            {
                ["Parameters__temporal_address"] = "example.tmprl.cloud:7233",
                ["Parameters__temporal_namespace"] = "example.namespace",
                ["Parameters__temporal_api_key"] = "not-a-real-key",
                ["Parameters__temporal_ui_address"] = "https://cloud.temporal.io/namespaces/example.namespace"
            },
            useAppHostConfiguration: true).ConfigureAwait(true);
        var resources = JsonNode.Parse(manifest)?["resources"]?.AsObject();

        Assert.NotNull(resources);
        Assert.DoesNotContain("temporal", resources);
        Assert.True(resources["temporal-api-key"]?["inputs"]?["value"]?["secret"]?.GetValue<bool>());
        Assert.Equal("{temporal-ui-address.value}", resources["sample-worker"]?["env"]?["TEMPORAL_UI_ADDRESS"]?.GetValue<string>());
        Assert.Equal("{temporal-ui-address.value}", resources["sample-client"]?["env"]?["TEMPORAL_UI_ADDRESS"]?.GetValue<string>());
    }

    private static bool IntegrationTestsEnabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable(IntegrationTestEnvironmentVariable),
            "1",
            StringComparison.Ordinal);

    private static TemporalResourceOptions CreateOptions() => new()
    {
        TargetHost = $"127.0.0.1:{GetAvailablePort()}",
        UIPort = GetAvailablePort(),
        MetricsPort = GetAvailablePort()
    };

    private static ProcessStartInfo CreateProcessStartInfo(
        string fileName, IEnumerable<string> arguments, string? workingDirectory)
    {
        var isAspire = fileName == "aspire";
        var startInfo = new ProcessStartInfo(isAspire ? "dotnet" : fileName)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? FindRepositoryRoot()
        };
        if (isAspire)
        {
            foreach (var argument in new[] { "tool", "run", "aspire", "--" })
                startInfo.ArgumentList.Add(argument);
        }
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static Process StartProcess(
        string fileName,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        string? workingDirectory = null)
    {
        var startInfo = CreateProcessStartInfo(fileName, arguments, workingDirectory);
        startInfo.RedirectStandardError = true;

        if (environmentVariables is not null)
        {
            foreach (var (key, value) in environmentVariables)
                startInfo.Environment[key] = value;
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException($"Unable to start '{fileName}'.");
    }

    private static async Task AssertDockerAvailableAsync()
    {
        using var process = StartProcess("docker", ["info"]);
        await process.WaitForExitAsync().ConfigureAwait(false);

        var standardError = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        Assert.True(process.ExitCode == 0, standardError);
    }

    private static async Task AssertContainerPortMappingAsync(string? containerId, int targetPort, int hostPort)
    {
        var result = await RunProcessAsync(
            "docker",
            ["port", containerId!, $"{targetPort}/tcp"]).ConfigureAwait(true);

        Assert.Contains($":{hostPort}", result.StandardOutput, StringComparison.Ordinal);
    }

    private static bool DefaultPortsAreAvailable() =>
        IsPortAvailable(TemporalResourceConstants.DefaultServiceEndpointPort) &&
        IsPortAvailable(TemporalResourceConstants.DefaultUIEndpointPort) &&
        IsPortAvailable(TemporalResourceConstants.DefaultMetricsEndpointPort);

    private static int GetResourcePort(JsonNode resource, string endpointName)
    {
        var urls = resource["urls"]?.AsArray() ?? throw new Xunit.Sdk.XunitException("Resource has no endpoint URLs.");
        var endpoint = urls.Single(url =>
            string.Equals(url?["name"]?.GetValue<string>(), endpointName, StringComparison.Ordinal));
        var value = endpoint?["url"]?.GetValue<string>() ??
            throw new Xunit.Sdk.XunitException($"Endpoint '{endpointName}' has no URL.");

        return new Uri(value).Port;
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static JsonArray ParseDescribeResources(string output)
    {
        var jsonStart = output.IndexOf('{', StringComparison.Ordinal);
        Assert.True(jsonStart >= 0, $"Aspire describe did not return JSON:{Environment.NewLine}{output}");

        return JsonNode.Parse(output[jsonStart..])?["resources"]?.AsArray() ??
            throw new Xunit.Sdk.XunitException("Aspire describe JSON has no resources array.");
    }

    private static Func<JsonNode?, bool> ResourceHasDisplayName(string displayName) =>
        resource => string.Equals(
            resource?["displayName"]?.GetValue<string>(),
            displayName,
            StringComparison.Ordinal);

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        bool assertSuccess = true)
    {
        var startInfo = CreateProcessStartInfo(fileName, arguments, workingDirectory);
        startInfo.RedirectStandardError = true;
        startInfo.RedirectStandardOutput = true;

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException($"Unable to start '{fileName}'.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException exception)
        {
            process.Kill(entireProcessTree: true);
            throw new Xunit.Sdk.XunitException($"'{fileName}' did not exit within two minutes.", exception);
        }

        var result = new ProcessResult(
            process.ExitCode,
            await standardOutputTask.ConfigureAwait(true),
            await standardErrorTask.ConfigureAwait(true));
        if (assertSuccess)
        {
            Assert.True(
                result.ExitCode == 0,
                $"'{fileName}' exited with code {result.ExitCode}.{Environment.NewLine}" +
                $"stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}" +
                $"stderr:{Environment.NewLine}{result.StandardError}");
        }

        return result;
    }

    private static async Task WaitForTemporalAsync(int port)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        Exception? lastException = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var client = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions
                {
                    TargetHost = $"127.0.0.1:{port}",
                    Namespace = "default"
                }).ConfigureAwait(false);
                await client.WorkflowService.GetSystemInfoAsync(new GetSystemInfoRequest()).ConfigureAwait(false);
                return;
            }
#pragma warning disable CA1031 // Connection failures are expected while the dev server starts.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                lastException = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
            }
        }

        throw new Xunit.Sdk.XunitException($"Temporal gRPC endpoint did not become ready: {lastException}");
    }

    private static async Task WaitForSuccessAsync(Uri uri)
    {
        using var client = new HttpClient();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        HttpStatusCode? lastStatusCode = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(uri).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return;

                lastStatusCode = response.StatusCode;
            }
            catch (HttpRequestException)
            {
                // The server is still starting.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
        }

        throw new Xunit.Sdk.XunitException($"Endpoint '{uri}' did not become ready. Last status: {lastStatusCode}");
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TemporalAspire.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Unable to locate the TemporalAspire repository root.");
    }

    private static async Task<string> PublishManifestAsync(
        string appHostName,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        bool useAppHostConfiguration = false)
    {
        await ManifestLock.WaitAsync().ConfigureAwait(true);
        try
        {
            var outputDirectory = Path.Combine(Path.GetTempPath(), $"temporal-aspire-manifest-{Guid.NewGuid():N}");
            var configPath = Path.Combine(FindRepositoryRoot(), "samples", "aspire.config.json");
            var originalConfig = await File.ReadAllTextAsync(configPath).ConfigureAwait(true);
            Directory.CreateDirectory(outputDirectory);

            try
            {
                var appHostDirectory = Path.Combine(
                    FindRepositoryRoot(),
                    "samples",
                    appHostName);
                var arguments = new List<string>
                {
                    "do",
                    "publish-manifest"
                };
                if (!useAppHostConfiguration)
                {
                    arguments.Add("--apphost");
                    arguments.Add(Path.Combine(appHostDirectory, $"{appHostName}.csproj"));
                }

                arguments.Add("--output-path");
                arguments.Add(outputDirectory);
                arguments.Add("--non-interactive");
                arguments.Add("--nologo");

                using var process = StartProcess(
                    "aspire",
                    arguments,
                    environmentVariables,
                    useAppHostConfiguration ? appHostDirectory : null);

                await process.WaitForExitAsync().ConfigureAwait(true);
                var standardError = await process.StandardError.ReadToEndAsync().ConfigureAwait(true);
                Assert.True(process.ExitCode == 0, standardError);

                var manifestPath = Directory.EnumerateFiles(outputDirectory, "*.json", SearchOption.AllDirectories)
                    .Single(path => File.ReadAllText(path).Contains("\"resources\"", StringComparison.Ordinal));
                return await File.ReadAllTextAsync(manifestPath).ConfigureAwait(true);
            }
            finally
            {
                try
                {
                    Directory.Delete(outputDirectory, recursive: true);
                }
                finally
                {
                    await File.WriteAllTextAsync(configPath, originalConfig).ConfigureAwait(true);
                }
            }
        }
        finally
        {
            ManifestLock.Release();
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

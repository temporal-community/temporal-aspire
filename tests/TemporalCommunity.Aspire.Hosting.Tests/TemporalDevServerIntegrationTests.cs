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

public class TemporalDevServerIntegrationTests
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
            await StopProcessAsync(process).ConfigureAwait(true);
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

    private static Process StartProcess(
        string fileName,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (workingDirectory is not null)
            startInfo.WorkingDirectory = workingDirectory;

        if (environmentVariables is not null)
        {
            foreach (var (key, value) in environmentVariables)
                startInfo.Environment[key] = value;
        }

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return Process.Start(startInfo) ?? throw new InvalidOperationException($"Unable to start '{fileName}'.");
    }

    private static async Task AssertDockerAvailableAsync()
    {
        using var process = StartProcess("docker", ["info"]);
        await process.WaitForExitAsync().ConfigureAwait(false);

        var standardError = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        Assert.True(process.ExitCode == 0, standardError);
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
}

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using TemporalCommunity.Aspire.Hosting;
using Xunit;

namespace TemporalCommunity.Aspire.Hosting.Tests;

public class TemporalCliServerResourceExtensionsTests
{
    // -----------------------------------------------------------------------
    // TemporalCliLocator — PATH guard that backs AddTemporalCliServer
    // -----------------------------------------------------------------------
    [Fact]
    public void EnsureAvailable_ThrowsInvalidOperationException_WhenCliNotFound()
    {
        // The isAvailable override lets tests simulate a machine without 'temporal' installed
        // without manipulating the real PATH environment variable.
        var ex = Assert.Throws<InvalidOperationException>(
            () => TemporalCliLocator.EnsureAvailable(() => false));

        Assert.Contains("temporal", ex.Message);
        Assert.Contains("https://", ex.Message);
    }

    [Fact]
    public void EnsureAvailable_DoesNotThrow_WhenCliIsFound()
    {
        // Should complete without exception when the check reports the CLI is present.
        TemporalCliLocator.EnsureAvailable(() => true);
    }

    [Fact]
    public void EnsureAvailable_ErrorMessage_ContainsInstallInstructions()
    {
        // Regression: the error must stay actionable — users must know where to get the CLI.
        var ex = Assert.Throws<InvalidOperationException>(
            () => TemporalCliLocator.EnsureAvailable(() => false));

        Assert.Contains("docs.temporal.io/cli", ex.Message);
        Assert.Contains("PATH", ex.Message);
    }

    // -----------------------------------------------------------------------
    // AddTemporalCliServer — registration structure (PATH-independent)
    //
    // These tests use TemporalCliServerResource directly instead of calling
    // AddTemporalCliServer so they do not depend on 'temporal' being installed
    // on the test machine.
    // -----------------------------------------------------------------------
    [Fact]
    public void TemporalCliServerResource_Command_IsTemporalExecutable()
    {
        // The resource MUST use "temporal" as the Aspire executable command.
        // Regression guard: a rename here would silently break all CLI server setups
        // because Aspire would try to launch the wrong binary.
        var resource = new TemporalCliServerResource("temporal-cli-server");

        Assert.Equal("temporal", resource.Command);
    }

    [Fact]
    public void TemporalCliServerResource_DefaultArgs_ContainServerStartDev()
    {
        // Aspire launches the resource by appending args to the Command.
        // The server subcommand must be present or temporal starts in the wrong mode.
        var resource = new TemporalCliServerResource("temporal-cli-server");
        var args = TemporalArgsBuilder.BuildArgs(resource.Options);

        Assert.Contains("server", args);
        Assert.Contains("start-dev", args);
    }

    [Fact]
    public void TemporalCliServerResource_DefaultArgs_ContainServicePort()
    {
        // The --port flag must map to the default service port so the Aspire-registered
        // endpoint matches the port temporal actually binds.
        var resource = new TemporalCliServerResource("temporal-cli-server");
        var args = TemporalArgsBuilder.BuildArgs(resource.Options);

        var portIndex = Array.IndexOf(args, "--port");
        Assert.True(portIndex >= 0, "Expected --port flag in args");
        Assert.Equal(
            TemporalResourceConstants.DefaultServiceEndpointPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            args[portIndex + 1]);
    }

    [Fact]
    public void TemporalCliServerResource_DefaultArgs_ContainUiPort()
    {
        // Regression: UI port must be emitted in CLI mode so the dashboard is reachable.
        // This was added alongside the fix that wires the UI endpoint URL in the Aspire dashboard.
        var resource = new TemporalCliServerResource("temporal-cli-server");
        var args = TemporalArgsBuilder.BuildArgs(resource.Options);

        var uiPortIndex = Array.IndexOf(args, "--ui-port");
        Assert.True(uiPortIndex >= 0, "Expected --ui-port flag in args");
        Assert.Equal(
            TemporalResourceConstants.DefaultUIEndpointPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            args[uiPortIndex + 1]);
    }

    [Fact]
    public void TemporalCliServerResource_NamespaceOption_IsReflectedInArgs()
    {
        var resource = new TemporalCliServerResource("temporal-cli-server");
        resource.Options.Namespace = "orders";

        var args = TemporalArgsBuilder.BuildArgs(resource.Options);

        Assert.Contains("orders", args);
    }

    [Fact]
    public void BuildArgs_IncludesDatabaseFilename_WhenConfigured()
    {
        var options = new TemporalResourceOptions();
        options.DevServerOptions.DatabaseFilename = "/tmp/temporal.db";

        var args = TemporalArgsBuilder.BuildArgs(options);

        var databaseFilenameIndex = Array.IndexOf(args, "--db-filename");
        Assert.True(databaseFilenameIndex >= 0, "Expected --db-filename flag in args");
        Assert.Equal("/tmp/temporal.db", args[databaseFilenameIndex + 1]);
    }

    [Fact]
    public void BuildArgs_IncludesDatabaseFilename_ForFixedContainerArgs()
    {
        var options = new TemporalResourceOptions();
        options.DevServerOptions.DatabaseFilename = "/home/temporal/temporal.db";

        var args = TemporalArgsBuilder.BuildArgs(options, fixedIpAndPort: true);

        var databaseFilenameIndex = Array.IndexOf(args, "--db-filename");
        Assert.True(databaseFilenameIndex >= 0, "Expected --db-filename flag in args");
        Assert.Equal("/home/temporal/temporal.db", args[databaseFilenameIndex + 1]);
    }

    [Fact]
    public void BuildArgs_UsesConfiguredMetricsPort()
    {
        var options = new TemporalResourceOptions { TargetHost = "127.0.0.1:17233", MetricsPort = 19233 };

        var args = TemporalArgsBuilder.BuildArgs(options);

        var metricsPortIndex = Array.IndexOf(args, "--metrics-port");
        Assert.True(metricsPortIndex >= 0, "Expected --metrics-port flag in args");
        Assert.Equal("19233", args[metricsPortIndex + 1]);
    }

    [Fact]
    public void BuildArgs_UsesFixedContainerMetricsPort()
    {
        var options = new TemporalResourceOptions { MetricsPort = 19233 };

        var args = TemporalArgsBuilder.BuildArgs(options, fixedIpAndPort: true);

        var metricsPortIndex = Array.IndexOf(args, "--metrics-port");
        Assert.True(metricsPortIndex >= 0, "Expected --metrics-port flag in args");
        Assert.Equal(
            TemporalResourceConstants.DefaultMetricsEndpointPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            args[metricsPortIndex + 1]);
    }

    [Fact]
    public void BuildArgs_HeadlessModeOmitsUiPort()
    {
        var options = new TemporalResourceOptions { UI = false };

        var args = TemporalArgsBuilder.BuildArgs(options);

        Assert.Contains("--headless", args);
        Assert.DoesNotContain("--ui-port", args);
    }

    [Fact]
    public void BuildArgs_DoesNotPassClientCredentialFlagsToDevServer()
    {
        var options = new TemporalResourceOptions
        {
            ApiKey = "api-key",
            CodecAuth = "codec-auth",
            CodecEndpoint = "http://localhost:8088"
        };

        var args = TemporalArgsBuilder.BuildArgs(options);

        Assert.DoesNotContain("--api-key", args);
        Assert.DoesNotContain("--codec-auth", args);
        Assert.DoesNotContain("--codec-endpoint", args);
    }

    [Fact]
    public void BuildArgs_OmitsDatabaseFilename_WhenNotConfigured()
    {
        var options = new TemporalResourceOptions();

        var args = TemporalArgsBuilder.BuildArgs(options);

        Assert.DoesNotContain("--db-filename", args);
    }

    // -----------------------------------------------------------------------
    // AddTemporalCliServer — public path tests (PATH-independent via seam)
    // -----------------------------------------------------------------------
    [Fact]
    public void AddTemporalCliServer_Throws_WhenTemporalCliNotOnPath()
    {
        // Regression guard: the public extension method must throw early with an
        // actionable error when the Temporal CLI is absent, rather than failing later
        // with a cryptic FailedToStart from Aspire. The isTemporalCliAvailable seam
        // lets tests simulate a machine without 'temporal' on PATH.
        var appBuilder = DistributedApplication.CreateBuilder([]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => appBuilder.AddTemporalCliServer("temporal", configure: null, isTemporalCliAvailable: () => false));

        Assert.Contains("temporal", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PATH", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddTemporalCliServer_RegistersExplicitPortsOnAllEndpoints()
    {
        // Regression: the CLI resource must register explicit host ports on all endpoints
        // so the Aspire dashboard renders clickable URLs. Without an explicit port the
        // dashboard assigns a random port and the link is either blank or wrong.
        var appBuilder = DistributedApplication.CreateBuilder([]);

        var resourceBuilder = appBuilder.AddTemporalCliServer(
            "temporal",
            configure: options =>
            {
                options.TargetHost = "0.0.0.0:17233";
                options.UIPort = 18233;
                options.MetricsPort = 19233;
            },
            isTemporalCliAvailable: () => true);

        var annotations = resourceBuilder.Resource.Annotations
            .OfType<EndpointAnnotation>()
            .ToList();

        var service = annotations.Single(e => e.Name == TemporalResourceConstants.ServiceEndpointName);
        var ui = annotations.Single(e => e.Name == TemporalResourceConstants.UIEndpointName);
        var metrics = annotations.Single(e => e.Name == TemporalResourceConstants.MetricsEndpointName);

        Assert.Equal(17233, service.Port);
        Assert.Equal(18233, ui.Port);
        Assert.Equal(19233, metrics.Port);
    }

    [Fact]
    public void AddTemporalCliServer_HeadlessModeDoesNotPublishUiEndpoint()
    {
        var appBuilder = DistributedApplication.CreateBuilder([]);

        var resourceBuilder = appBuilder.AddTemporalCliServer(
            "temporal",
            configure: options => options.UI = false,
            isTemporalCliAvailable: () => true);

        var endpointNames = resourceBuilder.Resource.Annotations
            .OfType<EndpointAnnotation>()
            .Select(endpoint => endpoint.Name)
            .ToList();

        Assert.DoesNotContain(TemporalResourceConstants.UIEndpointName, endpointNames);
        Assert.DoesNotContain(
            resourceBuilder.Resource.Annotations,
            annotation => annotation is ResourceUrlAnnotation or ResourceUrlsCallbackAnnotation);
    }
}

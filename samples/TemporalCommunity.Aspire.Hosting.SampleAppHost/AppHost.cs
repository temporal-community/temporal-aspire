using TemporalCommunity.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var appHostDirectory = FindAncestor(AppContext.BaseDirectory, "TemporalCommunity.Aspire.Hosting.SampleAppHost");
var temporalDataDirectory = Path.Combine(Directory.GetParent(appHostDirectory)!.FullName, ".temporal");
Directory.CreateDirectory(temporalDataDirectory);

var temporal = builder.AddTemporalLocalDevServer("temporal", options =>
{
    options.DevServerOptions.DatabaseFilename = Path.Combine(temporalDataDirectory, "temporal.db");
});

builder.AddProject<Projects.TemporalCommunity_Aspire_Hosting_SampleWorker>("sample-worker")
    .WaitFor(temporal)
    .WithReference(temporal);

builder.AddProject<Projects.TemporalCommunity_Aspire_Hosting_SampleClient>("sample-client")
    .WaitFor(temporal)
    .WithReference(temporal);

builder.Build().Run();

static string FindAncestor(string startPath, string directoryName)
{
    var directory = new DirectoryInfo(startPath);
    while (directory is not null)
    {
        if (string.Equals(directory.Name, directoryName, StringComparison.Ordinal))
            return directory.FullName;

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException($"Could not find ancestor directory '{directoryName}'.");
}

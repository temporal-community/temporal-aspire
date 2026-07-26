namespace TemporalCommunity.Aspire.Hosting;

/// <summary>
/// Represents an externally managed Temporal Cloud namespace.
/// </summary>
public class TemporalCloudResource(string name, ReferenceExpression address)
    : Resource(name), IResourceWithConnectionString
{
    /// <summary>Gets the Temporal Cloud host:port expression.</summary>
    public ReferenceExpression AddressExpression { get; } = address;

    /// <summary>Gets the connection string expression for dependent services.</summary>
    public ReferenceExpression ConnectionStringExpression => AddressExpression;

    /// <summary>Gets or sets the cloud resource configuration options.</summary>
    public TemporalCloudOptions Options { get; set; } = new();
}

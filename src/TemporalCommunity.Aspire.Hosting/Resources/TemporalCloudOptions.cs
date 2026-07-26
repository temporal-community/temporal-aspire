namespace TemporalCommunity.Aspire.Hosting;

/// <summary>
/// Configuration options for a Temporal Cloud resource.
/// </summary>
public class TemporalCloudOptions
{
    /// <summary>Gets or sets the Temporal Cloud namespace as a string or Aspire value provider.</summary>
    public object Namespace { get; set; } = "default";

    /// <summary>Gets or sets the API key value or parameter to inject into dependent resources.</summary>
    public object? ApiKey { get; set; }

    /// <summary>Gets or sets the optional Temporal Cloud UI address to inject into dependent resources.</summary>
    public object? UIAddress { get; set; }

    /// <summary>Gets or sets the codec authentication token for encrypted payloads.</summary>
    public string? CodecAuth { get; set; }

    /// <summary>Gets or sets the codec server endpoint for encrypted payloads.</summary>
    public string? CodecEndpoint { get; set; }
}

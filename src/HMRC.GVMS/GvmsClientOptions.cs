namespace HMRC.GVMS;

/// <summary>
/// Configuration for the HMRC Goods Vehicle Movement Service (GVMS) client.
/// </summary>
public sealed class GvmsClientOptions
{
    /// <summary>The HMRC production base URL.</summary>
    public const string ProductionBaseUrl = "https://api.service.hmrc.gov.uk/";

    /// <summary>The HMRC sandbox base URL, for integration testing against HMRC's test environment.</summary>
    public const string SandboxBaseUrl = "https://test-api.service.hmrc.gov.uk/";

    /// <summary>
    /// The <c>Accept</c> media type that selects the v1.0 representation of the HMRC API.
    /// HMRC versions its APIs through content negotiation rather than the URL path.
    /// </summary>
    public const string HmrcJsonMediaType = "application/vnd.hmrc.1.0+json";

    /// <summary>
    /// Base URL the client issues requests against. Defaults to <see cref="ProductionBaseUrl"/>;
    /// set to <see cref="SandboxBaseUrl"/> to target the HMRC sandbox.
    /// </summary>
    public Uri BaseUrl { get; set; } = new(ProductionBaseUrl);
}

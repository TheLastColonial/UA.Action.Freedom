namespace UA.Action.Freedom.Api.Configuration;

/// <summary>
/// Where documents and the customs work queue live.
/// </summary>
/// <remarks>
/// One storage account with prefixes, not an account per document type — see
/// <c>docs/recommendations.md</c> §1.
/// <para>
/// <see cref="ConnectionString"/> is the local-only escape hatch. In Azure the account is
/// reached by managed identity with shared-key authorisation disabled (§4.2), so this is
/// left empty there and the endpoint is resolved from <see cref="AccountName"/>.
/// </para>
/// </remarks>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Azurite / storage account connection string. Local development only.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Storage account name, used when authenticating by managed identity.</summary>
    public string? AccountName { get; set; }

    /// <summary>Container holding <c>manifests/</c>, <c>gmr/</c> and <c>elo/</c>.</summary>
    public string DocumentsContainer { get; set; } = "manifests";

    /// <summary>
    /// Container holding the ASP.NET Core data-protection key ring.
    /// Not optional in a scale-to-zero deployment — see <c>docs/recommendations.md</c> §3.2.
    /// </summary>
    public string DataProtectionContainer { get; set; } = "dataprotection";

    /// <summary>Queue the application hands GMR submissions to the Customs Worker on.</summary>
    public string CustomsQueue { get; set; } = "customs-work";

    /// <summary>Queue the Manifest Worker takes approved manifests from to render their documents.</summary>
    public string DocumentQueue { get; set; } = "manifest-documents";

    /// <summary>Queue the application hands French customs logistics envelopes to the worker on.</summary>
    public string EloQueue { get; set; } = "elo-envelopes";

    /// <summary>
    /// Container the Customs Worker writes issued envelopes and their barcode documents to, and the
    /// API reads them back from.
    /// </summary>
    public string EloContainer { get; set; } = "elo";

    /// <summary>
    /// Container holding the ICS2 Entry Summary Declaration recorded for each manifest, and the
    /// declarations superseded when one was invalidated and refiled.
    /// </summary>
    /// <remarks>
    /// The only container the API writes as well as reads. Nothing submits an ENS — ICS2's Shared
    /// Trader Interface speaks eDelivery AS4, which needs the always-on inbound access point §4.1
    /// declines — so there is no worker in this path and no queue (<c>docs/adr/0003</c>).
    /// </remarks>
    public string EnsContainer { get; set; } = "ens";

    /// <summary>Whether enough is configured to reach a storage account at all.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}

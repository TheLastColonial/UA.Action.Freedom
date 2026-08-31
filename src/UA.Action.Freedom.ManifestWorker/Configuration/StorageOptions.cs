namespace UA.Action.Freedom.ManifestWorker.Configuration;

/// <summary>Where the worker finds its queue and where it writes manifest documents.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Azurite / storage account connection string. Local development only.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Queue the Freedom Application hands approved manifests over on.</summary>
    public string DocumentQueue { get; set; } = "manifest-documents";

    /// <summary>Queue holding requests that failed in a way retrying will not fix.</summary>
    public string PoisonQueue { get; set; } = "manifest-documents-poison";

    /// <summary>Container holding the documents that travel with a vehicle.</summary>
    public string DocumentsContainer { get; set; } = "manifests";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}

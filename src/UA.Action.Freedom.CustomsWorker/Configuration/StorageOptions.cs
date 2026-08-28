namespace UA.Action.Freedom.CustomsWorker.Configuration;

/// <summary>Where the worker finds its queue and where it writes GMR documents.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Azurite / storage account connection string. Local development only.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Queue the Freedom Application hands GMR submissions over on.</summary>
    public string CustomsQueue { get; set; } = "customs-work";

    /// <summary>Queue holding submissions that failed in a way retrying will not fix.</summary>
    public string PoisonQueue { get; set; } = "customs-work-poison";

    /// <summary>Container holding issued Goods Movement Reference documents.</summary>
    public string GmrContainer { get; set; } = "gmr";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}

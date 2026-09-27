namespace UA.Action.Freedom.CustomsWorker.Configuration;

/// <summary>Where the worker finds its queues and where it writes border documents.</summary>
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

    /// <summary>Queue the Freedom Application hands French logistics envelopes over on.</summary>
    public string EloQueue { get; set; } = "elo-envelopes";

    /// <summary>
    /// Queue holding envelope requests that failed in a way retrying will not fix. Separate from
    /// <see cref="PoisonQueue"/> because a refusal from French customs and a refusal from HMRC need
    /// different people and different fixes.
    /// </summary>
    public string EloPoisonQueue { get; set; } = "elo-envelopes-poison";

    /// <summary>Container holding issued logistics envelopes and their barcode documents.</summary>
    public string EloContainer { get; set; } = "elo";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}

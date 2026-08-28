namespace UA.Action.Freedom.CustomsWorker.Customs;

/// <summary>
/// Where the Goods Movement Reference document is kept once HMRC has issued it.
/// </summary>
/// <remarks>
/// The <c>gmr/</c> prefix of the one document storage account
/// (<c>docs/recommendations.md</c> §1). Private, with downloads issued as short-lived
/// user-delegation SAS rather than served directly (§4.3).
/// </remarks>
public interface IGmrDocumentStore
{
    /// <summary>Stores the record for <paramref name="gmrId"/>, replacing any earlier version.</summary>
    Task SaveAsync(string gmrId, string content, CancellationToken cancellationToken);
}

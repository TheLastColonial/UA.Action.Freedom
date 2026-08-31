namespace UA.Action.Freedom.ManifestWorker.Documents;

/// <summary>Where the documents that travel with a vehicle are kept.</summary>
public interface IManifestDocumentStore
{
    /// <summary>Stores the document for <paramref name="manifestId"/>, replacing any earlier version.</summary>
    Task SaveAsync(string manifestId, string content, CancellationToken cancellationToken);
}

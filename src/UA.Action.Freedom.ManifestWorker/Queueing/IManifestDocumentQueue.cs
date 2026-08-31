namespace UA.Action.Freedom.ManifestWorker.Queueing;

/// <summary>One message on the manifest document queue.</summary>
/// <param name="MessageId">Queue-assigned identifier. Safe to log; the body is not.</param>
/// <param name="PopReceipt">Proof of this receipt, required to delete or update the message.</param>
/// <param name="Body">The document request, as the Freedom Application wrote it.</param>
public sealed record ManifestDocumentWorkItem(string MessageId, string PopReceipt, string Body);

/// <summary>The durable hand-off from the Freedom Application to this worker.</summary>
/// <remarks>
/// A port rather than a direct dependency on <c>Azure.Storage.Queues</c>, so the rules about
/// when a message is removed can be tested without a storage account — the same reasoning as
/// <c>ICustomsWorkQueue</c>. Getting those wrong means a vehicle leaves without its manifest.
/// </remarks>
public interface IManifestDocumentQueue
{
    Task<ManifestDocumentWorkItem?> ReceiveAsync(CancellationToken cancellationToken);

    Task CompleteAsync(ManifestDocumentWorkItem item, CancellationToken cancellationToken);

    Task DeadLetterAsync(ManifestDocumentWorkItem item, string reason, CancellationToken cancellationToken);
}

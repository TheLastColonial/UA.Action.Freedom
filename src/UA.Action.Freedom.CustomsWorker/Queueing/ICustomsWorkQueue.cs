namespace UA.Action.Freedom.CustomsWorker.Queueing;

/// <summary>
/// One message on the customs work queue.
/// </summary>
/// <param name="MessageId">Queue-assigned identifier. Safe to log; the body is not.</param>
/// <param name="PopReceipt">Proof of this receipt, required to delete or update the message.</param>
/// <param name="Body">The submission, as the Freedom Application wrote it.</param>
public sealed record CustomsWorkItem(string MessageId, string PopReceipt, string Body);

/// <summary>
/// The durable hand-off from the Freedom Application to the Customs Worker.
/// </summary>
/// <remarks>
/// A port rather than a direct dependency on <c>Azure.Storage.Queues</c>, so the rules
/// about when a message is removed can be tested without a storage account — those rules
/// are the interesting part, and getting them wrong loses a convoy's GMR.
/// </remarks>
public interface ICustomsWorkQueue
{
    /// <summary>
    /// Takes the next message, or <see langword="null"/> if the queue is empty. The message
    /// becomes invisible to other receivers but is not deleted, so a crash before
    /// <see cref="CompleteAsync"/> means it reappears rather than disappearing.
    /// </summary>
    Task<CustomsWorkItem?> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>Deletes the message. Only correct once the work is genuinely done.</summary>
    Task CompleteAsync(CustomsWorkItem item, CancellationToken cancellationToken);

    /// <summary>
    /// Moves the message to the poison queue. For failures that will recur identically on
    /// every retry — a malformed body, a submission HMRC has rejected on its merits.
    /// </summary>
    Task DeadLetterAsync(CustomsWorkItem item, string reason, CancellationToken cancellationToken);
}

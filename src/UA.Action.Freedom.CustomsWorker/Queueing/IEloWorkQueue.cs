namespace UA.Action.Freedom.CustomsWorker.Queueing;

/// <summary>One message on the French logistics envelope queue.</summary>
/// <param name="MessageId">Queue-assigned identifier. Safe to log; the body is not.</param>
/// <param name="PopReceipt">Proof of this receipt, required to delete or update the message.</param>
/// <param name="Body">The envelope request, as the Freedom Application wrote it.</param>
/// <param name="DequeueCount">How many times a worker has picked this up; above 1 it is a retry.</param>
/// <param name="InsertedOn">When the Freedom Application queued it, for how long it waited.</param>
public sealed record EloWorkItem(
    string MessageId,
    string PopReceipt,
    string Body,
    long DequeueCount = 1,
    DateTimeOffset? InsertedOn = null);

/// <summary>
/// The durable hand-off of an ELO envelope request from the Freedom Application to this worker.
/// </summary>
/// <remarks>
/// A port for the same reason as <see cref="ICustomsWorkQueue"/>: the rules about when a message is
/// deleted are the part that loses a convoy's paperwork if wrong, and they should be testable
/// without a storage account.
///
/// <para>
/// A separate port and a separate queue rather than another message type on the customs work queue.
/// Creating an envelope has a side effect at a different authority, fails for different reasons, and
/// needs its own poison queue — mixing them would mean one dead-letter pile that nobody can triage,
/// and a French refusal stalling a UK submission behind it.
/// </para>
/// </remarks>
public interface IEloWorkQueue
{
    Task<EloWorkItem?> ReceiveAsync(CancellationToken cancellationToken);

    Task CompleteAsync(EloWorkItem item, CancellationToken cancellationToken);

    Task DeadLetterAsync(EloWorkItem item, string reason, CancellationToken cancellationToken);
}

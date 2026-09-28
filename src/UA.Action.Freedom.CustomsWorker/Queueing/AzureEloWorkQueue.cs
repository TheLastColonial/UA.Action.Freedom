using Azure.Storage.Queues;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.CustomsWorker.Configuration;

namespace UA.Action.Freedom.CustomsWorker.Queueing;

/// <summary>
/// The envelope queue backed by Azure Queue Storage — Azurite locally, the real thing in Azure.
/// </summary>
public sealed class AzureEloWorkQueue(
    QueueServiceClient queues,
    IOptions<StorageOptions> options,
    ILogger<AzureEloWorkQueue> logger) : IEloWorkQueue
{
    private readonly StorageOptions _storage = options.Value;

    public async Task<EloWorkItem?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var queue = queues.GetQueueClient(_storage.EloQueue);

        // Long enough to create an envelope and store its barcode, short enough that a crashed
        // worker's message comes back quickly rather than stalling a convoy.
        var message = await queue.ReceiveMessageAsync(
            visibilityTimeout: TimeSpan.FromMinutes(2),
            cancellationToken: cancellationToken);

        return message.Value is null
            ? null
            : new EloWorkItem(
                message.Value.MessageId,
                message.Value.PopReceipt,
                message.Value.Body.ToString(),
                message.Value.DequeueCount,
                message.Value.InsertedOn);
    }

    public async Task CompleteAsync(EloWorkItem item, CancellationToken cancellationToken)
    {
        var queue = queues.GetQueueClient(_storage.EloQueue);
        await queue.DeleteMessageAsync(item.MessageId, item.PopReceipt, cancellationToken);
    }

    public async Task DeadLetterAsync(EloWorkItem item, string reason, CancellationToken cancellationToken)
    {
        // Copy to the poison queue before deleting the original: if the process dies between the
        // two, the message reappears on the work queue and is tried again. The other order loses it
        // outright.
        var poison = queues.GetQueueClient(_storage.EloPoisonQueue);
        await poison.SendMessageAsync(item.Body, cancellationToken);

        var queue = queues.GetQueueClient(_storage.EloQueue);
        await queue.DeleteMessageAsync(item.MessageId, item.PopReceipt, cancellationToken);

        logger.LogWarning(
            "Moved envelope request {MessageId} to {PoisonQueue}: {Reason}",
            item.MessageId,
            _storage.EloPoisonQueue,
            reason);
    }
}

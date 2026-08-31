using Azure.Storage.Queues;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.ManifestWorker.Configuration;

namespace UA.Action.Freedom.ManifestWorker.Queueing;

/// <summary>Azure Storage Queues behind <see cref="IManifestDocumentQueue"/>.</summary>
public sealed class AzureManifestDocumentQueue(
    QueueServiceClient queues,
    IOptions<StorageOptions> options,
    ILogger<AzureManifestDocumentQueue> logger) : IManifestDocumentQueue
{
    private readonly StorageOptions _storage = options.Value;

    public async Task<ManifestDocumentWorkItem?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var queue = queues.GetQueueClient(_storage.DocumentQueue);

        // Long enough to render and store a document, short enough that a crashed worker's
        // message comes back quickly rather than leaving a vehicle without paperwork.
        var message = await queue.ReceiveMessageAsync(
            visibilityTimeout: TimeSpan.FromMinutes(2), cancellationToken: cancellationToken);

        return message.Value is null
            ? null
            : new ManifestDocumentWorkItem(
                message.Value.MessageId, message.Value.PopReceipt, message.Value.Body.ToString());
    }

    public async Task CompleteAsync(ManifestDocumentWorkItem item, CancellationToken cancellationToken)
    {
        var queue = queues.GetQueueClient(_storage.DocumentQueue);
        await queue.DeleteMessageAsync(item.MessageId, item.PopReceipt, cancellationToken);
    }

    public async Task DeadLetterAsync(
        ManifestDocumentWorkItem item, string reason, CancellationToken cancellationToken)
    {
        // Copy to the poison queue before deleting the original: if the process dies between the
        // two, the message reappears on the work queue and is tried again. The other order loses
        // it outright.
        var poison = queues.GetQueueClient(_storage.PoisonQueue);
        await poison.SendMessageAsync(item.Body, cancellationToken);

        var queue = queues.GetQueueClient(_storage.DocumentQueue);
        await queue.DeleteMessageAsync(item.MessageId, item.PopReceipt, cancellationToken);

        logger.LogWarning("Moved work item {MessageId} to {PoisonQueue}: {Reason}",
            item.MessageId, _storage.PoisonQueue, reason);
    }
}

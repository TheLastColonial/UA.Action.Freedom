using Azure.Storage.Queues;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.CustomsWorker.Configuration;

namespace UA.Action.Freedom.CustomsWorker.Queueing;

/// <summary>
/// The customs work queue backed by Azure Queue Storage — Azurite locally, the real thing
/// in Azure. The SDK calls are identical either way; only the credential differs.
/// </summary>
public sealed class AzureCustomsWorkQueue(
    QueueServiceClient queues,
    IOptions<StorageOptions> options,
    ILogger<AzureCustomsWorkQueue> logger) : ICustomsWorkQueue
{
    private readonly StorageOptions _storage = options.Value;

    public async Task<CustomsWorkItem?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var queue = queues.GetQueueClient(_storage.CustomsQueue);

        // Long enough to submit to HMRC and delete the message, short enough that a crashed
        // worker's message comes back quickly rather than stalling a convoy.
        var message = await queue.ReceiveMessageAsync(
            visibilityTimeout: TimeSpan.FromMinutes(2),
            cancellationToken: cancellationToken);

        return message.Value is null
            ? null
            : new CustomsWorkItem(
                message.Value.MessageId,
                message.Value.PopReceipt,
                message.Value.Body.ToString());
    }

    public async Task CompleteAsync(CustomsWorkItem item, CancellationToken cancellationToken)
    {
        var queue = queues.GetQueueClient(_storage.CustomsQueue);
        await queue.DeleteMessageAsync(item.MessageId, item.PopReceipt, cancellationToken);
    }

    public async Task DeadLetterAsync(CustomsWorkItem item, string reason, CancellationToken cancellationToken)
    {
        // Copy to the poison queue before deleting the original: if the process dies
        // between the two, the message reappears on the work queue and is tried again.
        // The other order loses it outright.
        var poison = queues.GetQueueClient(_storage.PoisonQueue);
        await poison.SendMessageAsync(item.Body, cancellationToken);

        var queue = queues.GetQueueClient(_storage.CustomsQueue);
        await queue.DeleteMessageAsync(item.MessageId, item.PopReceipt, cancellationToken);

        logger.LogWarning(
            "Moved work item {MessageId} to {PoisonQueue}: {Reason}",
            item.MessageId,
            _storage.PoisonQueue,
            reason);
    }
}

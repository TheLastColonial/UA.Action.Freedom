using System.Text.Json;
using Microsoft.Extensions.Logging;
using UA.Action.Freedom.ManifestWorker.Queueing;

namespace UA.Action.Freedom.ManifestWorker.Documents;

/// <summary>
/// Drains the manifest document queue: renders each approved manifest and stores the document
/// that will travel with the vehicle.
/// </summary>
/// <remarks>
/// The three-way disposition mirrors <c>GmrSubmissionProcessor</c>, and for the same reason —
/// getting it wrong means a vehicle leaves without its manifest:
/// <list type="bullet">
/// <item>stored — remove the message;</item>
/// <item>unreadable — dead-letter it, because retrying produces the same answer;</item>
/// <item>storage unreachable — leave it alone and let the visibility timeout bring it back.</item>
/// </list>
/// </remarks>
public sealed class ManifestDocumentProcessor(
    IManifestDocumentQueue queue,
    IManifestDocumentStore documents,
    ILogger<ManifestDocumentProcessor> logger)
{
    /// <summary>Matches the serialiser the Freedom Application writes the message with.</summary>
    private static readonly JsonSerializerOptions QueueMessageFormat = JsonSerializerOptions.Web;

    /// <summary>Renders and stores one queued manifest. False when the queue was empty.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var item = await queue.ReceiveAsync(cancellationToken);

        if (item is null)
        {
            return false;
        }

        ManifestDocumentRequest? request;

        try
        {
            request = JsonSerializer.Deserialize<ManifestDocumentRequest>(item.Body, QueueMessageFormat);
        }
        catch (JsonException exception)
        {
            // Note what could not be read, never what it said. A manifest-shaped message carries
            // consignee organisations, and logs are retained (recommendations §4.8).
            logger.LogError(exception, "Work item {MessageId} is not a readable manifest document request.",
                item.MessageId);
            await queue.DeadLetterAsync(item, "Message body could not be deserialised.", cancellationToken);
            return true;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.ManifestId))
        {
            logger.LogError("Work item {MessageId} carries no manifest reference.", item.MessageId);
            await queue.DeadLetterAsync(item, "Message body carries no manifest reference.", cancellationToken);
            return true;
        }

        try
        {
            await documents.SaveAsync(
                request.ManifestId,
                ManifestDocumentRenderer.Render(request),
                cancellationToken);

            logger.LogInformation("Stored the manifest document for {ManifestId}.", request.ManifestId);

            await queue.CompleteAsync(item, cancellationToken);
        }
        catch (Exception exception)
        {
            // Transient: storage unreachable, a timeout, a dropped connection. Leave the message
            // where it is — a manifest that is never regenerated is a vehicle at a border with
            // no paperwork, which is worse than doing the work twice.
            logger.LogWarning(exception,
                "Could not store the manifest document for {ManifestId}; leaving work item {MessageId} to be retried.",
                request.ManifestId, item.MessageId);
        }

        return true;
    }
}

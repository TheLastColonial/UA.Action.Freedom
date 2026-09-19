using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UA.Action.Freedom.ManifestWorker.Queueing;
using UA.Action.Freedom.ManifestWorker.Telemetry;
using UA.Action.Freedom.Telemetry;

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
    ILogger<ManifestDocumentProcessor> logger,
    QueueFlowMetrics? queueMetrics = null,
    ManifestWorkerMetrics? workerMetrics = null)
{
    private readonly QueueFlowMetrics _queueMetrics = queueMetrics ?? QueueFlowMetrics.Unobserved;
    private readonly ManifestWorkerMetrics _worker = workerMetrics ?? ManifestWorkerMetrics.Unobserved;

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

        _queueMetrics.Received(QueueNames.ManifestDocuments, item.InsertedOn, item.DequeueCount);

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
            await DeadLetter(item, "Message body could not be deserialised.", cancellationToken);
            return true;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.ManifestId))
        {
            logger.LogError("Work item {MessageId} carries no manifest reference.", item.MessageId);
            await DeadLetter(item, "Message body carries no manifest reference.", cancellationToken);
            return true;
        }

        // Everything from here — including the blob write's client span — belongs to this message.
        using var activity = QueueTelemetry.StartConsumer(
            QueueNames.ManifestDocuments, item.MessageId, request.Traceparent);
        using var scope = logger.BeginScope(
            "Manifest {ManifestId}, work item {MessageId}", request.ManifestId, item.MessageId);
        var rendered = false;
        var stored = false;
        var storing = Stopwatch.GetTimestamp();

        try
        {
            var rendering = Stopwatch.GetTimestamp();
            var document = ManifestDocumentRenderer.Render(request);
            rendered = true;
            _worker.Rendered(Stopwatch.GetElapsedTime(rendering), request.Lines?.Count ?? 0);

            storing = Stopwatch.GetTimestamp();
            await documents.SaveAsync(request.ManifestId, document, cancellationToken);

            stored = true;
            _worker.Stored(Stopwatch.GetElapsedTime(storing), succeeded: true);
            logger.LogInformation("Stored the manifest document for {ManifestId}.", request.ManifestId);

            await queue.CompleteAsync(item, cancellationToken);
            _queueMetrics.Settled(QueueNames.ManifestDocuments, QueueOutcome.Completed);
        }
        catch (Exception exception)
        {
            if (rendered && !stored)
            {
                _worker.Stored(Stopwatch.GetElapsedTime(storing), succeeded: false);
            }

            _queueMetrics.Settled(QueueNames.ManifestDocuments, QueueOutcome.LeftForRetry);
            activity?.SetStatus(ActivityStatusCode.Error);

            // Transient: storage unreachable, a timeout, a dropped connection. Leave the message
            // where it is — a manifest that is never regenerated is a vehicle at a border with
            // no paperwork, which is worse than doing the work twice.
            logger.LogWarning(exception,
                "Could not store the manifest document for {ManifestId}; leaving work item {MessageId} to be retried.",
                request.ManifestId, item.MessageId);
        }

        return true;
    }

    private async Task DeadLetter(ManifestDocumentWorkItem item, string reason, CancellationToken cancellationToken)
    {
        await queue.DeadLetterAsync(item, reason, cancellationToken);
        _queueMetrics.Settled(QueueNames.ManifestDocuments, QueueOutcome.DeadLettered);
    }
}

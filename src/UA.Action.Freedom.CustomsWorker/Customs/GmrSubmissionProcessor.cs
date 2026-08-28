using System.Text.Json;
using HMRC.GVMS;
using Microsoft.Extensions.Logging;
using UA.Action.Freedom.CustomsWorker.Queueing;

namespace UA.Action.Freedom.CustomsWorker.Customs;

/// <summary>
/// Takes one submission off the customs work queue and sends it to HMRC.
/// </summary>
/// <remarks>
/// The queue-triggered half of the Customs Worker. In Azure this is the body of a
/// queue-triggered Function; here it is called from
/// <see cref="CustomsWorkerService"/> on a poll loop. The decision this class exists to
/// make is not "how do I call HMRC" but "when is it safe to delete the message".
/// </remarks>
public sealed class GmrSubmissionProcessor(
    ICustomsWorkQueue queue,
    IGvmsClient gvms,
    ILogger<GmrSubmissionProcessor> logger)
{
    /// <summary>
    /// camelCase, and case-insensitive on the way in.
    /// </summary>
    /// <remarks>
    /// <see cref="JsonSerializerOptions.Default"/> matches property names case-sensitively
    /// against the C# names, so a producer writing ordinary camelCase JSON deserialises to
    /// an object with every property null — and the failure surfaces as "carries no
    /// manifest reference", every message poisoned, with nothing to suggest the cause is
    /// capitalisation. <see cref="JsonSerializerOptions.Web"/> is the same configuration
    /// ASP.NET Core uses, which is what the Freedom Application will serialise with.
    /// </remarks>
    private static readonly JsonSerializerOptions QueueMessageFormat = JsonSerializerOptions.Web;

    /// <summary>
    /// Processes at most one message.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if a message was taken off the queue, whatever became of it;
    /// <see langword="false"/> if the queue was empty. The caller uses this to decide
    /// whether to keep draining or to go back to sleep.
    /// </returns>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var item = await queue.ReceiveAsync(cancellationToken);

        if (item is null)
        {
            return false;
        }

        GmrSubmission? submission;

        try
        {
            submission = JsonSerializer.Deserialize<GmrSubmission>(item.Body, QueueMessageFormat);
        }
        catch (JsonException exception)
        {
            // Note what could not be read, never what it said: a manifest-shaped message
            // may carry personal data, and logs are retained (recommendations 4.8).
            logger.LogError(exception, "Work item {MessageId} is not a readable GMR submission.", item.MessageId);
            await queue.DeadLetterAsync(item, "Message body could not be deserialised.", cancellationToken);
            return true;
        }

        if (submission is null || string.IsNullOrWhiteSpace(submission.ManifestId))
        {
            logger.LogError("Work item {MessageId} carries no manifest reference.", item.MessageId);
            await queue.DeadLetterAsync(item, "Message body carries no manifest reference.", cancellationToken);
            return true;
        }

        try
        {
            await gvms.CreateGoodsMovementRecordAsync(ToRequest(submission), cancellationToken);

            logger.LogInformation(
                "Submitted a goods movement record for manifest {ManifestId}.", submission.ManifestId);

            await queue.CompleteAsync(item, cancellationToken);
        }
        catch (GvmsApiException exception) when (exception.StatusCode is >= 400 and < 500)
        {
            // HMRC has judged the submission itself. Retrying produces the same answer, so
            // this needs a person, not another attempt.
            logger.LogError(
                exception,
                "HMRC rejected the goods movement record for manifest {ManifestId} with {StatusCode}.",
                submission.ManifestId,
                exception.StatusCode);

            await queue.DeadLetterAsync(
                item, $"HMRC rejected the submission with {exception.StatusCode}.", cancellationToken);
        }
        catch (Exception exception)
        {
            // Transient: a timeout, a 5xx, a dropped connection. Leave the message alone —
            // its visibility timeout will expire and it will be tried again. Completing or
            // poisoning it here would throw away a request nobody has recorded elsewhere.
            logger.LogWarning(
                exception,
                "Could not reach HMRC for manifest {ManifestId}; leaving work item {MessageId} to be retried.",
                submission.ManifestId,
                item.MessageId);
        }

        return true;
    }

    private static GoodsMovementRecordRequest ToRequest(GmrSubmission submission) => new()
    {
        // Aid leaving the UK for Ukraine: always outbound, always accompanied — the
        // vehicles are themselves part of the donation and someone drives them.
        Direction = Direction.UK_OUTBOUND,
        HaulierType = HaulierType.STANDARD,
        IsUnaccompanied = false,
        VehicleRegNum = submission.VehicleRegistration,
        PlannedCrossing = new PlannedCrossing
        {
            RouteId = submission.RouteId,
            LocalDateTimeOfDeparture = submission.LocalDateTimeOfDeparture,
        },
    };
}

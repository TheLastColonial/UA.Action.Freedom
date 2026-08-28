using System.Text.Json;
using HMRC.PushPullNotifications;
using Microsoft.Extensions.Logging;

namespace UA.Action.Freedom.CustomsWorker.Customs;

/// <summary>
/// Collects Goods Movement Reference outcomes from HMRC's notification box.
/// </summary>
/// <remarks>
/// The timer-triggered half of the Customs Worker, and the reason the system exposes no
/// inbound endpoint to HMRC at all: it polls rather than being called
/// (<c>docs/recommendations.md</c> §4.1). Outcomes arrive in minutes rather than seconds,
/// which costs nothing operationally and removes a public webhook that would have to be
/// defended without a paid WAF.
/// </remarks>
public sealed class GmrOutcomeCollector(
    IPushPullNotificationsClient notifications,
    IGmrDocumentStore documents,
    string boxId,
    ILogger<GmrOutcomeCollector> logger)
{
    private const string Pending = "PENDING";

    /// <summary>
    /// Reads every unread notification, stores the outcomes and acknowledges what it dealt
    /// with.
    /// </summary>
    /// <returns>
    /// How many notifications were found. Zero means the box is empty, which is the signal
    /// to stop polling and fall idle — polling an empty box on a timer burns the free-tier
    /// grant and, if it touches the database, keeps that awake too (§2.3).
    /// </returns>
    public async Task<int> CollectAsync(CancellationToken cancellationToken)
    {
        var unread = await notifications.GetNotificationsAsync(
            boxId, status: Pending, fromDate: null, toDate: null, count: null,
            cancellationToken: cancellationToken);

        if (unread.Count == 0)
        {
            return 0;
        }

        var handled = new List<string>();

        foreach (var notification in unread)
        {
            if (await TryHandle(notification, cancellationToken))
            {
                handled.Add(notification.NotificationId);
            }
        }

        if (handled.Count > 0)
        {
            // Acknowledging is irreversible — HMRC will not send these again — so only
            // notifications that were genuinely dealt with go in this list. Anything that
            // failed for a transient reason stays in the box for the next poll.
            await notifications.AcknowledgeNotificationsAsync(
                boxId,
                new AcknowledgeNotificationsRequest { NotificationIds = handled },
                cancellationToken);
        }

        return unread.Count;
    }

    private async Task<bool> TryHandle(Notification notification, CancellationToken cancellationToken)
    {
        string? gmrId;

        try
        {
            // HMRC nests the outcome as an encoded JSON *string* rather than an object, so
            // it deserialises twice. This is the easiest thing in the integration to get
            // wrong, and it fails quietly: the notification looks handled either way.
            using var outcome = JsonDocument.Parse(notification.Message);
            gmrId = outcome.RootElement.TryGetProperty("gmrId", out var id) ? id.GetString() : null;
        }
        catch (JsonException exception)
        {
            logger.LogError(
                exception,
                "Notification {NotificationId} does not carry a readable outcome; acknowledging it so it stops being polled.",
                notification.NotificationId);

            // Unparseable now means unparseable forever. Left unacknowledged it sits at the
            // head of the box and keeps the worker — and the timer — awake indefinitely.
            return true;
        }

        if (string.IsNullOrWhiteSpace(gmrId))
        {
            logger.LogError(
                "Notification {NotificationId} carries no gmrId; acknowledging it so it stops being polled.",
                notification.NotificationId);

            return true;
        }

        try
        {
            await documents.SaveAsync(gmrId, notification.Message, cancellationToken);

            logger.LogInformation("Stored the outcome for goods movement record {GmrId}.", gmrId);

            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not store the outcome for {GmrId}; leaving notification {NotificationId} unacknowledged.",
                gmrId,
                notification.NotificationId);

            return false;
        }
    }
}

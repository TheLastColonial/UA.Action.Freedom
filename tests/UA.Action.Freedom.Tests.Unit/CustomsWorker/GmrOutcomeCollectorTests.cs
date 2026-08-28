using AwesomeAssertions;
using HMRC.PushPullNotifications;
using MELT;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UA.Action.Freedom.CustomsWorker.Customs;

namespace UA.Action.Freedom.Tests.Unit.CustomsWorker;

/// <summary>
/// How the Customs Worker collects GMR outcomes from HMRC.
/// </summary>
/// <remarks>
/// Pull, never push: the worker holds outbound-only credentials and exposes no endpoint
/// for HMRC to call (docs/recommendations.md §4.1). Acknowledgement is what empties the
/// box, so acknowledging something that was not actually stored loses the outcome
/// permanently — HMRC will not send it again.
/// </remarks>
public class GmrOutcomeCollectorTests
{
    private const string BoxId = "1c5b9365-18a6-55a5-99c9-83a091ac7f26";

    [Fact]
    public async Task Stores_the_goods_movement_record_carried_in_a_notification()
    {
        var documents = Substitute.For<IGmrDocumentStore>();
        var collector = CollectorFor(ANotification(), documents);

        await collector.CollectAsync(CancellationToken.None);

        await documents.Received(1).SaveAsync("GMRLOCAL0001", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reads_the_outcome_out_of_the_json_string_HMRC_nests_it_in()
    {
        // HMRC puts the payload in `message` as an encoded JSON *string*, not as an object,
        // so it has to be deserialised twice. Getting this wrong is the likeliest
        // integration bug and it fails silently — the notification looks handled.
        var documents = Substitute.For<IGmrDocumentStore>();
        var collector = CollectorFor(ANotification(), documents);

        await collector.CollectAsync(CancellationToken.None);

        await documents.Received(1).SaveAsync(
            "GMRLOCAL0001",
            Arg.Is<string>(content => content.Contains("\"state\"") && content.Contains("OPEN")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Acknowledges_a_notification_it_has_stored()
    {
        var notifications = Substitute.For<IPushPullNotificationsClient>();
        notifications
            .GetNotificationsAsync(BoxId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns([ANotification()]);

        var collector = new GmrOutcomeCollector(
            notifications, Substitute.For<IGmrDocumentStore>(), BoxId,
            TestLoggerFactory.Create().CreateLogger<GmrOutcomeCollector>());

        await collector.CollectAsync(CancellationToken.None);

        await notifications.Received(1).AcknowledgeNotificationsAsync(
            BoxId,
            Arg.Is<AcknowledgeNotificationsRequest>(request =>
                request.NotificationIds.Contains("1ed5f407-8a11-4c8f-8a2d-1a8b1c4d0001")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Leaves_a_notification_unacknowledged_when_it_could_not_be_stored()
    {
        // Acknowledging is irreversible: HMRC never resends. If the blob write failed, the
        // outcome must stay in the box so the next poll picks it up again.
        var notifications = Substitute.For<IPushPullNotificationsClient>();
        notifications
            .GetNotificationsAsync(BoxId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns([ANotification()]);

        var documents = Substitute.For<IGmrDocumentStore>();
        documents.SaveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("storage unavailable"));

        var collector = new GmrOutcomeCollector(
            notifications, documents, BoxId, TestLoggerFactory.Create().CreateLogger<GmrOutcomeCollector>());

        await collector.CollectAsync(CancellationToken.None);

        await notifications.DidNotReceive().AcknowledgeNotificationsAsync(
            Arg.Any<string>(), Arg.Any<AcknowledgeNotificationsRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Acknowledges_a_notification_it_cannot_make_sense_of_rather_than_polling_it_forever()
    {
        // An unparseable message will never become parseable. Left unacknowledged it sits
        // at the head of the box and keeps the worker awake, which on the free tier also
        // keeps the database awake (recommendations 2.3).
        var notifications = Substitute.For<IPushPullNotificationsClient>();
        notifications
            .GetNotificationsAsync(BoxId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns([ANotification(message: "not json at all")]);

        var documents = Substitute.For<IGmrDocumentStore>();
        var collector = new GmrOutcomeCollector(
            notifications, documents, BoxId, TestLoggerFactory.Create().CreateLogger<GmrOutcomeCollector>());

        await collector.CollectAsync(CancellationToken.None);

        await documents.DidNotReceive().SaveAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await notifications.Received(1).AcknowledgeNotificationsAsync(
            BoxId, Arg.Any<AcknowledgeNotificationsRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_not_call_acknowledge_when_the_box_is_empty()
    {
        var notifications = Substitute.For<IPushPullNotificationsClient>();
        notifications
            .GetNotificationsAsync(BoxId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var collector = new GmrOutcomeCollector(
            notifications, Substitute.For<IGmrDocumentStore>(), BoxId,
            TestLoggerFactory.Create().CreateLogger<GmrOutcomeCollector>());

        var collected = await collector.CollectAsync(CancellationToken.None);

        collected.Should().Be(0);
        await notifications.DidNotReceive().AcknowledgeNotificationsAsync(
            Arg.Any<string>(), Arg.Any<AcknowledgeNotificationsRequest>(), Arg.Any<CancellationToken>());
    }

    private static Notification ANotification(string? message = null) => new()
    {
        NotificationId = "1ed5f407-8a11-4c8f-8a2d-1a8b1c4d0001",
        BoxId = BoxId,
        MessageContentType = MessageContentType.Application_json,
        Message = message ?? """{"gmrId":"GMRLOCAL0001","state":"OPEN","inspectionRequired":false}""",
        Status = Status.PENDING,
        CreatedDateTime = "2026-09-01T09:00:00.000Z",
    };

    private static GmrOutcomeCollector CollectorFor(Notification notification, IGmrDocumentStore documents)
    {
        var notifications = Substitute.For<IPushPullNotificationsClient>();
        notifications
            .GetNotificationsAsync(BoxId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns([notification]);

        return new GmrOutcomeCollector(
            notifications, documents, BoxId, TestLoggerFactory.Create().CreateLogger<GmrOutcomeCollector>());
    }
}

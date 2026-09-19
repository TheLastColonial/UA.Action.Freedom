using System.Diagnostics.Metrics;
using AwesomeAssertions;
using HMRC.PushPullNotifications;
using MELT;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UA.Action.Freedom.CustomsWorker.Customs;
using UA.Action.Freedom.CustomsWorker.Telemetry;
using UA.Action.Freedom.Tests.Unit.Telemetry;

namespace UA.Action.Freedom.Tests.Unit.CustomsWorker;

/// <summary>
/// What HMRC is reporting about our goods movement records, as a count by state — which is the
/// only view of "how many GMRs are open, embarked, completed" the system has without opening
/// the documents.
/// </summary>
public sealed class GmrOutcomeCollectorTelemetryTests : IDisposable
{
    private const string BoxId = "1c5b9365-18a6-55a5-99c9-83a091ac7f26";

    private readonly Meter _meter = new("UA.Action.Freedom.Tests.CustomsOutcomes");
    private readonly MetricCapture _capture;
    private readonly IGmrDocumentStore _documents = Substitute.For<IGmrDocumentStore>();

    public GmrOutcomeCollectorTelemetryTests()
    {
        _capture = new MetricCapture(_meter);
    }

    public void Dispose()
    {
        _capture.Dispose();
        _meter.Dispose();
    }

    private static Notification ANotification(string message) => new()
    {
        NotificationId = "1ed5f407-8a11-4c8f-8a2d-1a8b1c4d0001",
        BoxId = BoxId,
        MessageContentType = MessageContentType.Application_json,
        Message = message,
        Status = Status.PENDING,
        CreatedDateTime = "2026-09-01T09:00:00.000Z",
    };

    private GmrOutcomeCollector CollectorFor(params Notification[] notifications)
    {
        var client = Substitute.For<IPushPullNotificationsClient>();
        client
            .GetNotificationsAsync(BoxId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(notifications);

        return new GmrOutcomeCollector(
            client, _documents, BoxId, TestLoggerFactory.Create().CreateLogger<GmrOutcomeCollector>(),
            new CustomsMetrics(_meter));
    }

    [Theory]
    [InlineData("OPEN")]
    [InlineData("CHECKED_IN")]
    [InlineData("EMBARKED")]
    [InlineData("COMPLETED")]
    [InlineData("NOT_FINALISABLE")]
    public async Task A_stored_outcome_is_counted_under_the_state_HMRC_reported(string state)
    {
        var collector = CollectorFor(ANotification($$"""{"gmrId":"GMRLOCAL0001","state":"{{state}}"}"""));

        await collector.CollectAsync(CancellationToken.None);

        _capture.Sum("freedom.gmr.outcome.notifications", ("result", "stored"), ("state", state)).Should().Be(1);
    }

    [Fact]
    public async Task A_state_that_is_not_one_HMRC_documents_is_reported_as_unknown_rather_than_minting_a_label()
    {
        var collector = CollectorFor(ANotification("""{"gmrId":"GMRLOCAL0001","state":"SOMETHING_NEW"}"""));

        await collector.CollectAsync(CancellationToken.None);

        _capture.Sum("freedom.gmr.outcome.notifications", ("result", "stored"), ("state", "unknown")).Should().Be(1);
        _capture.Measurements.SelectMany(m => m.Tags.Values).Should().NotContain("SOMETHING_NEW");
    }

    [Fact]
    public async Task An_outcome_with_no_state_is_still_counted()
    {
        var collector = CollectorFor(ANotification("""{"gmrId":"GMRLOCAL0001"}"""));

        await collector.CollectAsync(CancellationToken.None);

        _capture.Sum("freedom.gmr.outcome.notifications", ("result", "stored"), ("state", "none")).Should().Be(1);
    }

    [Fact]
    public async Task An_unreadable_notification_is_counted_as_unreadable()
    {
        var collector = CollectorFor(ANotification("not json at all"));

        await collector.CollectAsync(CancellationToken.None);

        _capture.Sum("freedom.gmr.outcome.notifications", ("result", "unreadable"), ("state", "none")).Should().Be(1);
    }

    [Fact]
    public async Task A_notification_with_no_gmr_id_is_counted_with_the_state_it_did_report()
    {
        var collector = CollectorFor(ANotification("""{"state":"OPEN"}"""));

        await collector.CollectAsync(CancellationToken.None);

        _capture.Sum("freedom.gmr.outcome.notifications", ("result", "no_gmr_id"), ("state", "OPEN")).Should().Be(1);
    }

    [Fact]
    public async Task An_outcome_that_could_not_be_stored_is_counted_as_store_failed()
    {
        _documents.SaveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("storage unavailable"));
        var collector = CollectorFor(ANotification("""{"gmrId":"GMRLOCAL0001","state":"OPEN"}"""));

        await collector.CollectAsync(CancellationToken.None);

        _capture.Sum("freedom.gmr.outcome.notifications", ("result", "store_failed"), ("state", "OPEN")).Should().Be(1);
    }

    [Fact]
    public async Task An_empty_box_records_nothing()
    {
        var collector = CollectorFor();

        await collector.CollectAsync(CancellationToken.None);

        _capture.Measurements.Should().BeEmpty();
    }
}

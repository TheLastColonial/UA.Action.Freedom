using System.Diagnostics;
using System.Diagnostics.Metrics;
using AwesomeAssertions;
using HMRC.GVMS;
using MELT;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UA.Action.Freedom.CustomsWorker.Customs;
using UA.Action.Freedom.CustomsWorker.Queueing;
using UA.Action.Freedom.CustomsWorker.Telemetry;
using UA.Action.Freedom.Telemetry;
using UA.Action.Freedom.Tests.Unit.Telemetry;

namespace UA.Action.Freedom.Tests.Unit.CustomsWorker;

/// <summary>
/// What an operator can see of the Customs Worker without reading its logs: how each message was
/// settled, why the poisoned ones were poisoned, how long HMRC took, and whether the trace that
/// began at the approval carries on into the submission.
/// </summary>
public sealed class GmrSubmissionProcessorTelemetryTests : IDisposable
{
    private const string Traceparent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly Meter _meter = new("UA.Action.Freedom.Tests.Customs");
    private readonly MetricCapture _capture;
    private readonly ActivityCapture _activities = new(QueueTelemetry.SourceName);
    private readonly ICustomsWorkQueue _queue = Substitute.For<ICustomsWorkQueue>();
    private readonly IGvmsClient _gvms = Substitute.For<IGvmsClient>();
    private readonly ITestLoggerFactory _logs = TestLoggerFactory.Create();

    public GmrSubmissionProcessorTelemetryTests()
    {
        _capture = new MetricCapture(_meter);
    }

    public void Dispose()
    {
        _capture.Dispose();
        _activities.Dispose();
        _meter.Dispose();
    }

    private GmrSubmissionProcessor Processor() => new(
        _queue,
        _gvms,
        _logs.CreateLogger<GmrSubmissionProcessor>(),
        new QueueFlowMetrics(_meter, new FixedTime(Now)),
        new CustomsMetrics(_meter));

    private static string Body(string manifestId = "MAN-0001", string? traceparent = null) =>
        $$"""
        {"manifestId":"{{manifestId}}","haulierEori":"GB123456789000","vehicleRegistration":"AB12CDE","routeId":"20000","localDateTimeOfDeparture":"2026-09-01T18:30"{{(traceparent is null ? "" : $",\"traceparent\":\"{traceparent}\"")}}}
        """;

    private void Receives(CustomsWorkItem? item) =>
        _queue.ReceiveAsync(Arg.Any<CancellationToken>()).Returns(item);

    private static GvmsApiException Rejection(int status, string response = "{}") =>
        new("Rejected", status, response, new Dictionary<string, IEnumerable<string>>(), null);

    [Fact]
    public async Task A_message_HMRC_accepted_is_counted_as_completed_and_timed_as_accepted()
    {
        Receives(new CustomsWorkItem("m1", "r", Body()));

        await Processor().ProcessNextAsync(CancellationToken.None);

        _capture.Sum("freedom.queue.messages.processed", ("queue", "customs-work"), ("outcome", "completed"))
            .Should().Be(1);
        _capture.Of("freedom.gmr.submission.duration").Should().ContainSingle()
            .Which.Tags["outcome"].Should().Be("accepted");
    }

    [Fact]
    public async Task A_submission_HMRC_rejected_is_dead_lettered_with_the_status_it_was_rejected_with()
    {
        Receives(new CustomsWorkItem("m1", "r", Body()));
        _gvms.CreateGoodsMovementRecordAsync(Arg.Any<GoodsMovementRecordRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Rejection(422));

        await Processor().ProcessNextAsync(CancellationToken.None);

        _capture.Sum("freedom.queue.messages.processed", ("outcome", "dead_lettered")).Should().Be(1);
        _capture.Sum(
                "freedom.gmr.dead_letters",
                ("reason", "hmrc_rejected"), ("http.response.status_code", 422))
            .Should().Be(1);
        _capture.Of("freedom.gmr.submission.duration").Single().Tags["outcome"].Should().Be("rejected");
    }

    [Fact]
    public async Task An_unreadable_message_is_dead_lettered_as_unreadable_with_no_status()
    {
        Receives(new CustomsWorkItem("m1", "r", "this is not json"));

        await Processor().ProcessNextAsync(CancellationToken.None);

        var deadLetter = _capture.Of("freedom.gmr.dead_letters").Should().ContainSingle().Subject;
        deadLetter.Tags["reason"].Should().Be("unreadable");
        deadLetter.Tags.Should().NotContainKey("http.response.status_code");
    }

    [Fact]
    public async Task A_message_with_no_manifest_is_dead_lettered_as_having_no_manifest_reference()
    {
        Receives(new CustomsWorkItem("m1", "r", Body(manifestId: "")));

        await Processor().ProcessNextAsync(CancellationToken.None);

        _capture.Sum("freedom.gmr.dead_letters", ("reason", "no_manifest_ref")).Should().Be(1);
    }

    [Fact]
    public async Task A_transient_failure_leaves_the_message_for_retry_and_is_counted_as_such()
    {
        Receives(new CustomsWorkItem("m1", "r", Body()));
        _gvms.CreateGoodsMovementRecordAsync(Arg.Any<GoodsMovementRecordRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        await Processor().ProcessNextAsync(CancellationToken.None);

        _capture.Sum("freedom.queue.messages.processed", ("outcome", "left_for_retry")).Should().Be(1);
        _capture.Of("freedom.gmr.submission.duration").Single().Tags["outcome"].Should().Be("error");
        _capture.Of("freedom.gmr.dead_letters").Should().BeEmpty();
    }

    [Fact]
    public async Task An_empty_queue_records_nothing()
    {
        Receives(null);

        await Processor().ProcessNextAsync(CancellationToken.None);

        _capture.Measurements.Should().BeEmpty();
    }

    [Fact]
    public async Task How_long_a_message_waited_and_whether_it_is_a_retry_is_recorded_when_it_is_picked_up()
    {
        Receives(new CustomsWorkItem("m1", "r", Body(), DequeueCount: 3, InsertedOn: Now.AddMinutes(-4)));

        await Processor().ProcessNextAsync(CancellationToken.None);

        _capture.Of("freedom.queue.message.age").Should().ContainSingle().Which.Value.Should().Be(240);
        _capture.Sum("freedom.queue.redeliveries", ("queue", "customs-work")).Should().Be(1);
    }

    [Fact]
    public async Task The_submission_is_traced_as_a_consumer_span_linked_to_the_approval_that_queued_it()
    {
        Receives(new CustomsWorkItem("m-link", "r", Body(traceparent: Traceparent)));

        await Processor().ProcessNextAsync(CancellationToken.None);

        var consumer = _activities.WithTag("messaging.message.id", "m-link").Should().ContainSingle().Subject;
        consumer.Kind.Should().Be(ActivityKind.Consumer);
        consumer.Links.Should().ContainSingle()
            .Which.Context.TraceId.ToString().Should().Be("0af7651916cd43dd8448eb211c80319c");
    }

    [Fact]
    public async Task The_call_to_HMRC_happens_inside_the_consumer_span_so_its_client_span_nests_there()
    {
        Receives(new CustomsWorkItem("m-nest", "r", Body(traceparent: Traceparent)));
        Activity? during = null;
        _gvms.CreateGoodsMovementRecordAsync(Arg.Any<GoodsMovementRecordRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                during = Activity.Current;
                return Task.CompletedTask;
            });

        await Processor().ProcessNextAsync(CancellationToken.None);

        during.Should().NotBeNull();
        during!.GetTagItem("messaging.message.id").Should().Be("m-nest");
    }

    [Fact]
    public async Task A_message_with_no_trace_context_is_still_processed_and_traced_unlinked()
    {
        Receives(new CustomsWorkItem("m-plain", "r", Body()));

        await Processor().ProcessNextAsync(CancellationToken.None);

        _activities.WithTag("messaging.message.id", "m-plain").Should().ContainSingle()
            .Which.Links.Should().BeEmpty();
    }

    [Theory]
    [InlineData(400)]
    [InlineData(503)]
    public async Task What_HMRC_said_in_a_failure_never_reaches_the_logs(int status)
    {
        // HMRC's error body can echo the vehicle and the haulier, and GvmsApiException puts up to
        // 512 characters of it in its message. Logs are retained, so log the status, not the body.
        Receives(new CustomsWorkItem("m1", "r", Body()));
        _gvms.CreateGoodsMovementRecordAsync(Arg.Any<GoodsMovementRecordRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Rejection(status, """{"message":"Vehicle AB12CDE for Olena Kovalenko, Kharkiv"}"""));

        await Processor().ProcessNextAsync(CancellationToken.None);

        var written = string.Join(
            " ",
            _logs.Sink.LogEntries.Select(entry => $"{entry.Message} {entry.Exception}"));
        written.Should().NotContain("Kovalenko").And.NotContain("Kharkiv");
        written.Should().Contain(status.ToString());
    }

    [Fact]
    public async Task The_manifest_and_message_are_on_every_log_line_written_while_it_is_processed()
    {
        Receives(new CustomsWorkItem("m-scope", "r", Body(manifestId: "MAN-SCOPE")));

        await Processor().ProcessNextAsync(CancellationToken.None);

        var submitted = _logs.Sink.LogEntries.Should().ContainSingle(entry => entry.Message!.Contains("Submitted")).Subject;
        var scope = string.Join(" ", submitted.Scopes.Select(s => s.Message));
        scope.Should().Contain("MAN-SCOPE").And.Contain("m-scope");
    }
}

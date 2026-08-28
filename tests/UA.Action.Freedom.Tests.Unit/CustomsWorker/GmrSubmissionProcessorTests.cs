
using AwesomeAssertions;
using HMRC.GVMS;
using MELT;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UA.Action.Freedom.CustomsWorker.Customs;
using UA.Action.Freedom.CustomsWorker.Queueing;

namespace UA.Action.Freedom.Tests.Unit.CustomsWorker;

/// <summary>
/// What the Customs Worker does with a manifest that a dispatcher has asked for a Goods
/// Movement Reference for.
/// </summary>
/// <remarks>
/// The queue is the only durable record that the request was made, so the rules about when
/// a message is removed matter more than the submission itself: remove it too early and a
/// convoy silently loses its GMR, too late and HMRC gets the same movement twice.
/// </remarks>
public class GmrSubmissionProcessorTests
{
    [Fact]
    public async Task Submits_a_goods_movement_record_for_a_queued_manifest()
    {
        var gvms = Substitute.For<IGvmsClient>();
        var processor = ProcessorFor(gvms, AQueuedSubmission());

        await processor.ProcessNextAsync(CancellationToken.None);

        await gvms.Received(1).CreateGoodsMovementRecordAsync(
            Arg.Is<GoodsMovementRecordRequest>(request =>
                request.Direction == Direction.UK_OUTBOUND &&
                request.VehicleRegNum == "AB12CDE"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Removes_the_work_item_from_the_queue_once_HMRC_has_accepted_it()
    {
        var queue = Substitute.For<ICustomsWorkQueue>();
        var item = AQueuedSubmission();
        queue.ReceiveAsync(Arg.Any<CancellationToken>()).Returns(item);

        var processor = new GmrSubmissionProcessor(
            queue, Substitute.For<IGvmsClient>(), TestLoggerFactory.Create().CreateLogger<GmrSubmissionProcessor>());

        await processor.ProcessNextAsync(CancellationToken.None);

        await queue.Received(1).CompleteAsync(item, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Leaves_the_work_item_on_the_queue_when_HMRC_cannot_be_reached()
    {
        // A network failure says nothing about whether the submission was valid. Deleting
        // the message here would lose the request entirely; leaving it means the visibility
        // timeout expires and another attempt happens, which is the behaviour that makes
        // the queue a durable hand-off rather than a fire-and-forget.
        var queue = Substitute.For<ICustomsWorkQueue>();
        var item = AQueuedSubmission();
        queue.ReceiveAsync(Arg.Any<CancellationToken>()).Returns(item);

        var gvms = Substitute.For<IGvmsClient>();
        gvms.CreateGoodsMovementRecordAsync(Arg.Any<GoodsMovementRecordRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var processor = new GmrSubmissionProcessor(
            queue, gvms, TestLoggerFactory.Create().CreateLogger<GmrSubmissionProcessor>());

        await processor.ProcessNextAsync(CancellationToken.None);

        await queue.DidNotReceive().CompleteAsync(Arg.Any<CustomsWorkItem>(), Arg.Any<CancellationToken>());
        await queue.DidNotReceive().DeadLetterAsync(
            Arg.Any<CustomsWorkItem>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Moves_the_work_item_to_the_poison_queue_when_HMRC_rejects_the_submission()
    {
        // A 400 will fail identically on every retry, so retrying just burns the free-tier
        // grant and hides the problem. Poison it and let someone look.
        var queue = Substitute.For<ICustomsWorkQueue>();
        var item = AQueuedSubmission();
        queue.ReceiveAsync(Arg.Any<CancellationToken>()).Returns(item);

        var gvms = Substitute.For<IGvmsClient>();
        gvms.CreateGoodsMovementRecordAsync(Arg.Any<GoodsMovementRecordRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new GvmsApiException("Bad Request", 400, "{}", null, null));

        var processor = new GmrSubmissionProcessor(
            queue, gvms, TestLoggerFactory.Create().CreateLogger<GmrSubmissionProcessor>());

        await processor.ProcessNextAsync(CancellationToken.None);

        await queue.Received(1).DeadLetterAsync(item, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Moves_an_unreadable_work_item_to_the_poison_queue_without_calling_HMRC()
    {
        var queue = Substitute.For<ICustomsWorkQueue>();
        var item = new CustomsWorkItem("1", "receipt", "this is not json");
        queue.ReceiveAsync(Arg.Any<CancellationToken>()).Returns(item);

        var gvms = Substitute.For<IGvmsClient>();
        var processor = new GmrSubmissionProcessor(
            queue, gvms, TestLoggerFactory.Create().CreateLogger<GmrSubmissionProcessor>());

        await processor.ProcessNextAsync(CancellationToken.None);

        await gvms.DidNotReceive().CreateGoodsMovementRecordAsync(
            Arg.Any<GoodsMovementRecordRequest>(), Arg.Any<CancellationToken>());
        await queue.Received(1).DeadLetterAsync(item, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_nothing_at_all_when_the_queue_is_empty()
    {
        var queue = Substitute.For<ICustomsWorkQueue>();
        queue.ReceiveAsync(Arg.Any<CancellationToken>()).Returns((CustomsWorkItem?)null);

        var gvms = Substitute.For<IGvmsClient>();
        var processor = new GmrSubmissionProcessor(
            queue, gvms, TestLoggerFactory.Create().CreateLogger<GmrSubmissionProcessor>());

        var processed = await processor.ProcessNextAsync(CancellationToken.None);

        processed.Should().BeFalse();
        await gvms.DidNotReceive().CreateGoodsMovementRecordAsync(
            Arg.Any<GoodsMovementRecordRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Never_logs_the_receiver_detail_a_manifest_might_carry()
    {
        // Volunteer and receiver personal data must stay out of logs (recommendations 4.8
        // and 4.4). The work item body is the one place a queue message could carry it, so
        // the failure path must name the message, not quote it.
        var logger = TestLoggerFactory.Create();
        var queue = Substitute.For<ICustomsWorkQueue>();
        var item = new CustomsWorkItem("1", "receipt", """{"contactName":"Olena Kovalenko","city":"Kharkiv"}""");
        queue.ReceiveAsync(Arg.Any<CancellationToken>()).Returns(item);

        var processor = new GmrSubmissionProcessor(
            queue, Substitute.For<IGvmsClient>(), logger.CreateLogger<GmrSubmissionProcessor>());

        await processor.ProcessNextAsync(CancellationToken.None);

        var written = string.Join(" ", logger.Sink.LogEntries.Select(entry => entry.Message));
        written.Should().NotContain("Olena Kovalenko").And.NotContain("Kharkiv");
        written.Should().Contain("1");
    }

    /// <summary>
    /// The message exactly as it sits on the queue.
    /// </summary>
    /// <remarks>
    /// Written out as a literal rather than produced by a serialiser,
    /// deliberately. Round-tripping through the serialiser proves only that this code
    /// agrees with itself: it would keep passing while the producer wrote camelCase and the
    /// consumer expected PascalCase, which is exactly the mismatch that reaches the queue
    /// and silently poisons every message. The literal pins the wire contract instead.
    /// </remarks>
    private const string QueuedSubmissionJson =
        """
        {
          "manifestId": "MAN-0001",
          "haulierEori": "GB123456789000",
          "vehicleRegistration": "AB12CDE",
          "routeId": "20000",
          "localDateTimeOfDeparture": "2026-09-01T18:30"
        }
        """;

    private static CustomsWorkItem AQueuedSubmission() => new("1", "receipt", QueuedSubmissionJson);

    private static GmrSubmissionProcessor ProcessorFor(IGvmsClient gvms, CustomsWorkItem item)
    {
        var queue = Substitute.For<ICustomsWorkQueue>();
        queue.ReceiveAsync(Arg.Any<CancellationToken>()).Returns(item);

        return new GmrSubmissionProcessor(
            queue, gvms, TestLoggerFactory.Create().CreateLogger<GmrSubmissionProcessor>());
    }
}

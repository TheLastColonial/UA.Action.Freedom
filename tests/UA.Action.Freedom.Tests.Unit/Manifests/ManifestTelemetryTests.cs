using System.Diagnostics.Metrics;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using MELT;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Application.Telemetry;
using UA.Action.Freedom.Domain;
using UA.Action.Freedom.Tests.Unit.Telemetry;

namespace UA.Action.Freedom.Tests.Unit.Manifests;

/// <summary>
/// What an operator can learn about the manifest lifecycle without opening the database: which
/// edges are being taken, which are being refused, and — the one that matters most — whether an
/// approval froze a manifest and then failed to hand its paperwork to a worker.
/// </summary>
public sealed class ManifestTelemetryTests : IDisposable
{
    private const string Id = "MAN-0001";

    private static readonly DateTime Stamped = new(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

    private readonly Meter _meter = new(FreedomMetrics.MeterName);
    private readonly MetricCapture _capture;
    private readonly FreedomMetrics _metrics;

    public ManifestTelemetryTests()
    {
        _capture = new MetricCapture(_meter);
        _metrics = new FreedomMetrics(_meter);
    }

    public void Dispose()
    {
        _capture.Dispose();
        _meter.Dispose();
    }

    private static ManifestReadModel AManifest(ManifestStatus status) => new(
        Id, "WVWZZZ1JZXW000001", 42, status, null, FerryBookingComplete: false, GmrSubmittedAt: null);

    private static IConvoyRepository AConvoy()
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(42, Arg.Any<CancellationToken>()).Returns(
            new ConvoyReadModel(42, Stamped, Stamped.AddDays(4), Stamped.AddDays(-5)));
        return convoys;
    }

    private static IManifestRepository AProposedManifest()
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(AManifest(ManifestStatus.Proposed));
        repository.ConfirmAndFreezeAsync(Id, ManifestStatus.Proposed, Arg.Any<CancellationToken>()).Returns(Stamped);
        return repository;
    }

    [Fact]
    public async Task A_refused_edge_is_counted_with_where_it_started_and_where_it_was_going()
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(AManifest(ManifestStatus.Created));
        var handler = new TransitionManifestHandler(repository, AConvoy(), _metrics);

        await handler.HandleAsync(new TransitionManifestCommand(Id, ManifestStatus.Delivered), CancellationToken.None);

        _capture.Sum(
                "freedom.manifest.transitions",
                ("from", "Created"), ("to", "Delivered"), ("outcome", "IllegalTransition"))
            .Should().Be(1);
    }

    [Fact]
    public async Task A_transition_that_happened_is_counted_as_transitioned()
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(AManifest(ManifestStatus.Confirmed));
        repository.TransitionAsync(Id, ManifestStatus.Confirmed, ManifestStatus.Preparing, Arg.Any<CancellationToken>())
            .Returns(true);
        var handler = new TransitionManifestHandler(repository, AConvoy(), _metrics);

        await handler.HandleAsync(new TransitionManifestCommand(Id, ManifestStatus.Preparing), CancellationToken.None);

        _capture.Sum(
                "freedom.manifest.transitions",
                ("from", "Confirmed"), ("to", "Preparing"), ("outcome", "Transitioned"))
            .Should().Be(1);
    }

    [Fact]
    public async Task A_transition_of_a_manifest_that_does_not_exist_has_no_starting_state()
    {
        var repository = Substitute.For<IManifestRepository>();
        var handler = new TransitionManifestHandler(repository, AConvoy(), _metrics);

        await handler.HandleAsync(new TransitionManifestCommand("MAN-NOPE", ManifestStatus.Proposed), CancellationToken.None);

        _capture.Sum(
                "freedom.manifest.transitions", ("from", "none"), ("to", "Proposed"), ("outcome", "NotFound"))
            .Should().Be(1);
    }

    [Fact]
    public async Task An_approval_is_counted_as_the_edge_into_confirmed()
    {
        var handler = new ApproveManifestHandler(
            AProposedManifest(), AConvoy(), Substitute.For<IManifestWorkQueue>(), _metrics);

        await handler.HandleAsync(new ApproveManifestCommand(Id), CancellationToken.None);

        _capture.Sum(
                "freedom.manifest.transitions",
                ("from", "Proposed"), ("to", "Confirmed"), ("outcome", "Transitioned"))
            .Should().Be(1);
    }

    [Fact]
    public async Task An_approval_that_froze_the_manifest_but_could_not_queue_the_gmr_is_counted_and_logged()
    {
        var logs = TestLoggerFactory.Create();
        var queue = Substitute.For<IManifestWorkQueue>();
        queue.EnqueueGmrSubmissionAsync(Arg.Any<GmrSubmissionRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Storage:ConnectionString is not configured"));
        var handler = new ApproveManifestHandler(
            AProposedManifest(), AConvoy(), queue, _metrics, logs.CreateLogger<ApproveManifestHandler>());

        var approve = () => handler.HandleAsync(new ApproveManifestCommand(Id), CancellationToken.None);

        await approve.Should().ThrowAsync<InvalidOperationException>();
        _capture.Sum("freedom.manifest.approve.partial_failures", ("stage", "gmr")).Should().Be(1);
        logs.Sink.LogEntries.Should().ContainSingle(entry =>
            entry.LogLevel == LogLevel.Warning && entry.Message!.Contains(Id));
    }

    [Fact]
    public async Task An_approval_whose_document_could_not_be_queued_is_counted_at_the_document_stage()
    {
        var queue = Substitute.For<IManifestWorkQueue>();
        queue.EnqueueDocumentAsync(Arg.Any<ManifestDocumentRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("no storage"));
        var repository = AProposedManifest();
        repository.GetDocumentLinesAsync(Id, Arg.Any<CancellationToken>()).Returns([]);
        var handler = new ApproveManifestHandler(repository, AConvoy(), queue, _metrics);

        var approve = () => handler.HandleAsync(new ApproveManifestCommand(Id), CancellationToken.None);

        await approve.Should().ThrowAsync<InvalidOperationException>();
        _capture.Sum("freedom.manifest.approve.partial_failures", ("stage", "document")).Should().Be(1);
    }

    [Fact]
    public async Task A_successful_approval_records_no_partial_failure()
    {
        var repository = AProposedManifest();
        repository.GetDocumentLinesAsync(Id, Arg.Any<CancellationToken>()).Returns([]);
        var handler = new ApproveManifestHandler(repository, AConvoy(), Substitute.For<IManifestWorkQueue>(), _metrics);

        await handler.HandleAsync(new ApproveManifestCommand(Id), CancellationToken.None);

        _capture.Of("freedom.manifest.approve.partial_failures").Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, "found")]
    [InlineData(false, "not_found")]
    public async Task Resolving_a_receiver_address_is_counted_without_saying_whose(bool exists, string label)
    {
        var receiverRef = Guid.NewGuid();
        var detail = Substitute.For<IReceiverDetailRepository>();
        detail.ResolveAsync(receiverRef, "ground-officer-sub", "loading day", Arg.Any<CancellationToken>())
            .Returns(exists
                ? new ReceiverDetailReadModel(receiverRef, "Olena Kovalenko", "+380501234567", "12 Vulytsia Sumska", null, "Kharkiv", "61002", null)
                : null);
        var handler = new GetReceiverDetailHandler(detail, _metrics);

        await handler.HandleAsync(
            new GetReceiverDetailQuery(receiverRef, "ground-officer-sub", "loading day"), CancellationToken.None);

        _capture.Sum("freedom.receiver.detail.resolves", ("result", label)).Should().Be(1);
        _capture.Measurements
            .SelectMany(m => m.Tags.Values.Select(v => v?.ToString() ?? string.Empty))
            .Should().NotContain(v => v.Contains("Kovalenko") || v.Contains("ground-officer") || v.Contains(receiverRef.ToString()));
    }
}

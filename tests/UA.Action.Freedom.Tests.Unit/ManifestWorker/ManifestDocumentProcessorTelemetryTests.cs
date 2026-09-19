using System.Diagnostics;
using System.Diagnostics.Metrics;
using AwesomeAssertions;
using MELT;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UA.Action.Freedom.ManifestWorker.Documents;
using UA.Action.Freedom.ManifestWorker.Queueing;
using UA.Action.Freedom.ManifestWorker.Telemetry;
using UA.Action.Freedom.Telemetry;
using UA.Action.Freedom.Tests.Unit.Telemetry;

namespace UA.Action.Freedom.Tests.Unit.ManifestWorker;

/// <summary>
/// What an operator can see of document rendering: whether approved manifests are turning into
/// documents, how long that takes, and — because the document is the thing that crosses borders —
/// that nothing about its consignees leaks into a metric or a span on the way.
/// </summary>
public sealed class ManifestDocumentProcessorTelemetryTests : IDisposable
{
    private const string Traceparent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly Meter _meter = new("UA.Action.Freedom.Tests.ManifestWorker");
    private readonly MetricCapture _capture;
    private readonly ActivityCapture _activities = new(QueueTelemetry.SourceName);
    private readonly IManifestDocumentQueue _queue = Substitute.For<IManifestDocumentQueue>();
    private readonly IManifestDocumentStore _documents = Substitute.For<IManifestDocumentStore>();
    private readonly ITestLoggerFactory _logs = TestLoggerFactory.Create();

    public ManifestDocumentProcessorTelemetryTests()
    {
        _capture = new MetricCapture(_meter);
    }

    public void Dispose()
    {
        _capture.Dispose();
        _activities.Dispose();
        _meter.Dispose();
    }

    private ManifestDocumentProcessor Processor() => new(
        _queue,
        _documents,
        _logs.CreateLogger<ManifestDocumentProcessor>(),
        new QueueFlowMetrics(_meter, new FixedTime(Now)),
        new ManifestWorkerMetrics(_meter));

    private static string Body(string manifestId = "MAN-0001", string? traceparent = null) =>
        $$"""
        {"manifestId":"{{manifestId}}","vehicleRegistration":"AB12CDE","vehicleWeightKg":1400,"cargoKg":42,"crewAndBagsKg":200,"fuelKg":45,"totalKg":1687,"lines":[{"boxId":1,"weightKg":30,"itemCount":4,"receiverOrganisation":"Kharkiv Regional Hospital","receiverRegion":"Kharkiv oblast"},{"boxId":2,"weightKg":12,"itemCount":2,"receiverOrganisation":"Poltava Childrens Home","receiverRegion":"Poltava oblast"}]{{(traceparent is null ? "" : $",\"traceparent\":\"{traceparent}\"")}}}
        """;

    private void Receives(ManifestDocumentWorkItem? item) =>
        _queue.ReceiveAsync(Arg.Any<CancellationToken>()).Returns(item);

    [Fact]
    public async Task A_stored_document_is_counted_as_completed_and_timed_at_both_stages()
    {
        Receives(new ManifestDocumentWorkItem("m1", "r", Body()));

        await Processor().ProcessNextAsync(CancellationToken.None);

        _capture.Sum("freedom.queue.messages.processed", ("queue", "manifest-documents"), ("outcome", "completed"))
            .Should().Be(1);
        _capture.Of("freedom.manifest.document.render.duration").Should().ContainSingle();
        _capture.Of("freedom.manifest.document.store.duration").Should().ContainSingle()
            .Which.Tags["result"].Should().Be("ok");
    }

    [Fact]
    public async Task How_many_boxes_a_document_listed_is_recorded_as_its_size()
    {
        Receives(new ManifestDocumentWorkItem("m1", "r", Body()));

        await Processor().ProcessNextAsync(CancellationToken.None);

        _capture.Of("freedom.manifest.document.lines").Should().ContainSingle().Which.Value.Should().Be(2);
    }

    [Fact]
    public async Task A_document_that_could_not_be_stored_is_left_for_retry_and_its_store_is_timed_as_an_error()
    {
        Receives(new ManifestDocumentWorkItem("m1", "r", Body()));
        _documents.SaveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("storage unavailable"));

        await Processor().ProcessNextAsync(CancellationToken.None);

        _capture.Sum("freedom.queue.messages.processed", ("outcome", "left_for_retry")).Should().Be(1);
        _capture.Of("freedom.manifest.document.store.duration").Single().Tags["result"].Should().Be("error");
    }

    [Fact]
    public async Task An_unreadable_request_is_dead_lettered_and_nothing_is_rendered()
    {
        Receives(new ManifestDocumentWorkItem("m1", "r", "this is not json"));

        await Processor().ProcessNextAsync(CancellationToken.None);

        _capture.Sum("freedom.queue.messages.processed", ("outcome", "dead_lettered")).Should().Be(1);
        _capture.Of("freedom.manifest.document.render.duration").Should().BeEmpty();
    }

    [Fact]
    public async Task A_request_with_no_manifest_reference_is_dead_lettered()
    {
        Receives(new ManifestDocumentWorkItem("m1", "r", Body(manifestId: "")));

        await Processor().ProcessNextAsync(CancellationToken.None);

        _capture.Sum("freedom.queue.messages.processed", ("outcome", "dead_lettered")).Should().Be(1);
    }

    [Fact]
    public async Task An_empty_queue_records_nothing()
    {
        Receives(null);

        await Processor().ProcessNextAsync(CancellationToken.None);

        _capture.Measurements.Should().BeEmpty();
    }

    [Fact]
    public async Task How_long_a_request_waited_and_whether_it_is_a_retry_is_recorded_when_it_is_picked_up()
    {
        Receives(new ManifestDocumentWorkItem("m1", "r", Body(), DequeueCount: 2, InsertedOn: Now.AddSeconds(-45)));

        await Processor().ProcessNextAsync(CancellationToken.None);

        _capture.Of("freedom.queue.message.age").Should().ContainSingle().Which.Value.Should().Be(45);
        _capture.Sum("freedom.queue.redeliveries", ("queue", "manifest-documents")).Should().Be(1);
    }

    [Fact]
    public async Task Rendering_is_traced_as_a_consumer_span_linked_to_the_approval_that_queued_it()
    {
        Receives(new ManifestDocumentWorkItem("m-link", "r", Body(traceparent: Traceparent)));

        await Processor().ProcessNextAsync(CancellationToken.None);

        var consumer = _activities.WithTag("messaging.message.id", "m-link").Should().ContainSingle().Subject;
        consumer.Kind.Should().Be(ActivityKind.Consumer);
        consumer.GetTagItem("messaging.destination.name").Should().Be("manifest-documents");
        consumer.Links.Should().ContainSingle()
            .Which.Context.TraceId.ToString().Should().Be("0af7651916cd43dd8448eb211c80319c");
    }

    [Fact]
    public async Task Nothing_about_the_consignees_reaches_a_metric_or_a_span()
    {
        Receives(new ManifestDocumentWorkItem("m-leak", "r", Body(traceparent: Traceparent)));

        await Processor().ProcessNextAsync(CancellationToken.None);

        var tags = _capture.Measurements.SelectMany(m => m.Tags.Values.Select(v => v?.ToString() ?? string.Empty))
            .Concat(_activities.WithTag("messaging.message.id", "m-leak")
                .SelectMany(a => a.TagObjects.Select(t => t.Value?.ToString() ?? string.Empty)
                    .Append(a.DisplayName)));

        tags.Should().NotContain(value =>
            value.Contains("Kharkiv") || value.Contains("Poltava") || value.Contains("Regional Hospital"));
    }

    [Fact]
    public async Task The_manifest_and_message_are_on_every_log_line_written_while_it_is_processed()
    {
        Receives(new ManifestDocumentWorkItem("m-scope", "r", Body(manifestId: "MAN-SCOPE")));

        await Processor().ProcessNextAsync(CancellationToken.None);

        var stored = _logs.Sink.LogEntries.Should().ContainSingle(entry => entry.Message!.Contains("Stored")).Subject;
        var scope = string.Join(" ", stored.Scopes.Select(s => s.Message));
        scope.Should().Contain("MAN-SCOPE").And.Contain("m-scope");
    }
}

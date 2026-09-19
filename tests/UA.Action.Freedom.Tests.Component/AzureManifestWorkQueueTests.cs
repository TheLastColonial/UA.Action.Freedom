using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Azure;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Api.Messaging;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Telemetry;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// The hand-off from the Freedom Application to the two workers. What matters here is the
/// contract between processes: the JSON the workers read, and the trace context that lets one
/// trace follow an approval into them.
/// </summary>
public sealed class AzureManifestWorkQueueTests : IDisposable
{
    private const string CustomsQueue = "customs-work";
    private const string DocumentQueue = "manifest-documents";

    private readonly ActivityListener _listener;
    private readonly List<Activity> _producers = [];
    private readonly Meter _meter = new(QueueFlowMetrics.MeterName);
    private readonly List<(string Queue, string Result)> _enqueued = [];
    private readonly MeterListener _meters = new();
    private readonly QueueServiceClient _queues = Substitute.For<QueueServiceClient>();
    private readonly QueueClient _customs = Substitute.For<QueueClient>();
    private readonly QueueClient _documents = Substitute.For<QueueClient>();
    private readonly List<string> _sent = [];

    public AzureManifestWorkQueueTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == QueueTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_producers)
                {
                    _producers.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);

        _meters.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, _meter))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _meters.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            if (instrument.Name != "freedom.queue.enqueue")
            {
                return;
            }

            string? queue = null, result = null;

            foreach (var tag in tags)
            {
                if (tag.Key == "queue") queue = tag.Value?.ToString();
                if (tag.Key == "result") result = tag.Value?.ToString();
            }

            lock (_enqueued)
            {
                _enqueued.Add((queue!, result!));
            }
        });
        _meters.Start();

        _queues.GetQueueClient(CustomsQueue).Returns(_customs);
        _queues.GetQueueClient(DocumentQueue).Returns(_documents);
        _customs.SendMessageAsync(Arg.Do<string>(_sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response.FromValue(QueuesModelFactory.SendReceipt("id", default, default, "r", default), null!)));
        _documents.SendMessageAsync(Arg.Do<string>(_sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response.FromValue(QueuesModelFactory.SendReceipt("id", default, default, "r", default), null!)));
    }

    public void Dispose()
    {
        _listener.Dispose();
        _meters.Dispose();
        _meter.Dispose();
    }

    private AzureManifestWorkQueue Queue() => new(
        _queues,
        Options.Create(new StorageOptions { CustomsQueue = CustomsQueue, DocumentQueue = DocumentQueue }),
        Options.Create(new CustomsOptions { HaulierEori = "GB123456789000", RouteId = "1" }),
        new QueueFlowMetrics(_meter));

    private static GmrSubmissionRequest ASubmission(string manifestId) =>
        new(manifestId, "AB12 CDE", new DateTime(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task The_gmr_message_keeps_the_shape_the_customs_worker_reads()
    {
        await Queue().EnqueueGmrSubmissionAsync(ASubmission("MAN-SHAPE"), TestContext.Current.CancellationToken);

        var message = JsonDocument.Parse(_sent.Single(body => body.Contains("MAN-SHAPE"))).RootElement;

        message.GetProperty("manifestId").GetString().Should().Be("MAN-SHAPE");
        message.GetProperty("haulierEori").GetString().Should().Be("GB123456789000");
        message.GetProperty("vehicleRegistration").GetString().Should().Be("AB12 CDE");
        message.GetProperty("routeId").GetString().Should().Be("1");
        message.GetProperty("localDateTimeOfDeparture").GetString().Should().Be("2026-09-01T06:00");
    }

    [Fact]
    public async Task The_gmr_message_carries_the_trace_context_of_the_span_that_queued_it()
    {
        await Queue().EnqueueGmrSubmissionAsync(ASubmission("MAN-TRACE"), TestContext.Current.CancellationToken);

        var message = JsonDocument.Parse(_sent.Single(body => body.Contains("MAN-TRACE"))).RootElement;
        var producer = ProducerFor(CustomsQueue);

        message.GetProperty("traceparent").GetString().Should().Be(producer.Id);
    }

    [Fact]
    public async Task The_document_message_carries_the_trace_context_too()
    {
        var document = new ManifestDocumentRequest("MAN-DOC", "AB12 CDE", 2100, 380, 200, 45, 2725, []);

        await Queue().EnqueueDocumentAsync(document, TestContext.Current.CancellationToken);

        var message = JsonDocument.Parse(_sent.Single(body => body.Contains("MAN-DOC"))).RootElement;

        message.GetProperty("manifestId").GetString().Should().Be("MAN-DOC");
        message.GetProperty("traceparent").GetString().Should().Be(ProducerFor(DocumentQueue).Id);
    }

    [Fact]
    public async Task A_queued_message_is_counted_as_enqueued()
    {
        await Queue().EnqueueGmrSubmissionAsync(ASubmission("MAN-COUNT"), TestContext.Current.CancellationToken);

        lock (_enqueued)
        {
            _enqueued.Should().ContainSingle().Which.Should().Be((CustomsQueue, "ok"));
        }
    }

    [Fact]
    public async Task A_message_the_queue_refused_is_counted_as_failed_and_the_failure_still_propagates()
    {
        _customs.SendMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException("The queue is unavailable."));

        var enqueue = () => Queue().EnqueueGmrSubmissionAsync(ASubmission("MAN-FAIL"), TestContext.Current.CancellationToken);

        await enqueue.Should().ThrowAsync<RequestFailedException>();
        lock (_enqueued)
        {
            _enqueued.Should().ContainSingle().Which.Should().Be((CustomsQueue, "failed"));
        }
    }

    private Activity ProducerFor(string queue)
    {
        lock (_producers)
        {
            return _producers.Single(activity =>
                activity.Kind == ActivityKind.Producer
                && Equals(activity.GetTagItem("messaging.destination.name"), queue));
        }
    }
}

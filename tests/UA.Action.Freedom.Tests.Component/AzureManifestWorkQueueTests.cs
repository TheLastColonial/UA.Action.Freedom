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
using UA.Action.Freedom.Domain;
using UA.Action.Freedom.Telemetry;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// The hand-off from the Freedom Application to the workers. What matters here is the contract
/// between processes: the JSON the workers read, and the trace context that lets one trace follow
/// an approval into them.
/// </summary>
/// <remarks>
/// Every expected body is written as a literal and read back off <see cref="JsonDocument"/>, never
/// round-tripped through the serialiser this class uses. Round-tripping would prove only that the
/// code agrees with itself, and would keep passing while the producer wrote camelCase and a worker
/// expected PascalCase — which is exactly the mismatch that reaches the queue and silently poisons
/// every message.
/// </remarks>
public sealed class AzureManifestWorkQueueTests : IDisposable
{
    private const string CustomsQueue = "customs-work";
    private const string DocumentQueue = "manifest-documents";
    private const string EloQueue = "elo-envelopes";

    /// <summary>A stand-in ENS, as <c>Elo:PlaceholderDeclarationIdentifier</c> supplies today.</summary>
    private const string Declaration = "25FR17551780961AT5";

    private readonly ActivityListener _listener;
    private readonly List<Activity> _producers = [];
    private readonly Meter _meter = new(QueueFlowMetrics.MeterName);
    private readonly List<(string Queue, string Result)> _enqueued = [];
    private readonly MeterListener _meters = new();
    private readonly QueueServiceClient _queues = Substitute.For<QueueServiceClient>();
    private readonly QueueClient _customs = Substitute.For<QueueClient>();
    private readonly QueueClient _documents = Substitute.For<QueueClient>();
    private readonly QueueClient _envelopes = Substitute.For<QueueClient>();
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
        _queues.GetQueueClient(EloQueue).Returns(_envelopes);
        _customs.SendMessageAsync(Arg.Do<string>(_sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response.FromValue(QueuesModelFactory.SendReceipt("id", default, default, "r", default), null!)));
        _documents.SendMessageAsync(Arg.Do<string>(_sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response.FromValue(QueuesModelFactory.SendReceipt("id", default, default, "r", default), null!)));
        _envelopes.SendMessageAsync(Arg.Do<string>(_sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response.FromValue(QueuesModelFactory.SendReceipt("id", default, default, "r", default), null!)));
    }

    public void Dispose()
    {
        _listener.Dispose();
        _meters.Dispose();
        _meter.Dispose();
    }

    private AzureManifestWorkQueue Queue(string declaration = Declaration) => new(
        _queues,
        Options.Create(new StorageOptions
        {
            CustomsQueue = CustomsQueue,
            DocumentQueue = DocumentQueue,
            EloQueue = EloQueue,
        }),
        Options.Create(new CustomsOptions { HaulierEori = "GB123456789000", RouteId = "1" }),
        Options.Create(new EloOptions { PlaceholderDeclarationIdentifier = declaration }),
        new QueueFlowMetrics(_meter));

    private static GmrSubmissionRequest ASubmission(string manifestId) =>
        new(manifestId, "AB12 CDE", new DateTime(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc));

    private static EloEnvelopeRequest AnEnvelope(string manifestId) =>
        new(manifestId, EloCrossingProfile.HumanitarianAidToUkraine);

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

    /// <summary>
    /// The pairing information French customs uses to decide which formalities the envelope must
    /// contain. Every flag is named on the wire, because a missing boolean reads as false and the
    /// difference between TIR/ATA and not is an extra declaration a dispatcher has to obtain.
    /// </summary>
    [Fact]
    public async Task The_envelope_message_keeps_the_shape_the_customs_worker_reads()
    {
        await Queue().EnqueueEloEnvelopeAsync(AnEnvelope("MAN-ELO"), TestContext.Current.CancellationToken);

        var message = JsonDocument.Parse(_sent.Single(body => body.Contains("MAN-ELO"))).RootElement;

        message.GetProperty("manifestId").GetString().Should().Be("MAN-ELO");
        message.GetProperty("crossingDirection").GetString().Should().Be("Import");
        message.GetProperty("lorryType").GetString().Should().Be("Loaded");
        message.GetProperty("tirAta").GetBoolean().Should().BeTrue();
        message.GetProperty("hasTransportContract").GetBoolean().Should().BeFalse();
        message.GetProperty("postal").GetBoolean().Should().BeFalse();
        message.GetProperty("emptyPackaging").GetBoolean().Should().BeFalse();
        message.GetProperty("sanitaryOrPhytosanitary").GetBoolean().Should().BeFalse();
        message.GetProperty("fisheryProducts").GetBoolean().Should().BeFalse();
        message.GetProperty("declarationIdentifiers").EnumerateArray()
            .Select(identifier => identifier.GetString()).Should().Equal(Declaration);
    }

    /// <summary>
    /// An envelope describes a crossing, and the message has no field for a receiver, an address or
    /// a box. Asserted on the serialised body rather than on the type, because the body is what
    /// sits on a durable queue that outlives the request.
    /// </summary>
    [Fact]
    public async Task The_envelope_message_says_nothing_about_where_the_load_is_going()
    {
        await Queue().EnqueueEloEnvelopeAsync(AnEnvelope("MAN-REDACT"), TestContext.Current.CancellationToken);

        var message = JsonDocument.Parse(_sent.Single(body => body.Contains("MAN-REDACT"))).RootElement;

        message.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "manifestId", "crossingDirection", "lorryType", "tirAta", "hasTransportContract",
            "postal", "emptyPackaging", "sanitaryOrPhytosanitary", "fisheryProducts",
            "declarationIdentifiers", "traceparent");
    }

    [Fact]
    public async Task The_envelope_message_carries_the_trace_context_of_the_span_that_queued_it()
    {
        await Queue().EnqueueEloEnvelopeAsync(AnEnvelope("MAN-ELO-TRACE"), TestContext.Current.CancellationToken);

        var message = JsonDocument.Parse(_sent.Single(body => body.Contains("MAN-ELO-TRACE"))).RootElement;

        message.GetProperty("traceparent").GetString().Should().Be(ProducerFor(EloQueue).Id);
    }

    /// <summary>
    /// ENV_CTR_RG08: a loaded transport unit must name at least one declaration. Refused here
    /// rather than paying a round trip to French customs to be told the same thing — and the
    /// message has to name the missing setting, because by this point the manifest is frozen and
    /// somebody has to be able to fix it.
    /// </summary>
    [Fact]
    public async Task Refuses_an_envelope_for_a_loaded_lorry_that_names_no_declaration()
    {
        var enqueue = () => Queue(declaration: string.Empty)
            .EnqueueEloEnvelopeAsync(AnEnvelope("MAN-NO-DECL"), TestContext.Current.CancellationToken);

        (await enqueue.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*{nameof(EloOptions.PlaceholderDeclarationIdentifier)}*");
        _sent.Should().BeEmpty();
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

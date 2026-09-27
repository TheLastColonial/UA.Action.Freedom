using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using AwesomeAssertions;
using EDI.ELO;
using MELT;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UA.Action.Freedom.CustomsWorker.Elo;
using UA.Action.Freedom.CustomsWorker.Queueing;
using UA.Action.Freedom.CustomsWorker.Telemetry;
using UA.Action.Freedom.Telemetry;
using UA.Action.Freedom.Tests.Unit.Telemetry;

namespace UA.Action.Freedom.Tests.Unit.CustomsWorker;

/// <summary>
/// What creating a French logistics envelope looks like from outside the worker.
/// </summary>
/// <remarks>
/// Two things are being defended. First, that an approval and the envelope it caused can be found as
/// one trace, because the gap between "the dispatcher approved it" and "the lorry has a barcode" is
/// where this integration will be debugged. Second, that no manifest reference, plate or French
/// customs response body becomes a metric tag or a span attribute — those are unbounded, and one of
/// them quotes a declaration.
/// </remarks>
public sealed class EloEnvelopeProcessorTelemetryTests : IDisposable
{
    private const string Traceparent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
    private const string Jeton = "EI202512091201178668Z";
    private const string NumeroDossier = "B2025120912003386654";

    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Barcode = Encoding.ASCII.GetBytes("%PDF-1.4 barcode");

    private readonly Meter _meter = new("UA.Action.Freedom.Tests.Elo");
    private readonly MetricCapture _capture;
    private readonly ActivityCapture _activities = new(QueueTelemetry.SourceName);
    private readonly IEloWorkQueue _queue = Substitute.For<IEloWorkQueue>();
    private readonly IEloClient _elo = Substitute.For<IEloClient>();
    private readonly IEloDocumentStore _documents = Substitute.For<IEloDocumentStore>();
    private readonly ITestLoggerFactory _logs = TestLoggerFactory.Create();

    public EloEnvelopeProcessorTelemetryTests() => _capture = new MetricCapture(_meter);

    public void Dispose()
    {
        _capture.Dispose();
        _activities.Dispose();
        _meter.Dispose();
    }

    [Fact]
    public async Task A_created_envelope_is_counted_as_accepted_and_the_message_as_completed()
    {
        Accepts();
        Receives(AnItem("m-ok"));

        await Processor().ProcessNextAsync(TestContext.Current.CancellationToken);

        _capture.Of("freedom.elo.submission.duration").Should().ContainSingle()
            .Which.Tags["outcome"].Should().Be("accepted");
        _capture.Sum(
                "freedom.queue.messages.processed",
                ("queue", QueueNames.EloEnvelopes), ("outcome", "completed"))
            .Should().Be(1);
    }

    [Fact]
    public async Task A_refused_envelope_is_counted_as_rejected_with_the_status_that_refused_it()
    {
        Refuses(400);
        Receives(AnItem("m-refused"));

        await Processor().ProcessNextAsync(TestContext.Current.CancellationToken);

        _capture.Of("freedom.elo.submission.duration").Should().ContainSingle()
            .Which.Tags["outcome"].Should().Be("rejected");
        var deadLetter = _capture.Of("freedom.gmr.dead_letters").Should().ContainSingle().Subject;
        deadLetter.Tags["reason"].Should().Be("elo_rejected");
        deadLetter.Tags["http.response.status_code"].Should().Be(400);
    }

    [Fact]
    public async Task An_envelope_that_never_got_an_answer_is_counted_as_an_error_and_left_for_retry()
    {
        _elo.CreerENVAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ENV_CRE01>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));
        Receives(AnItem("m-error"));

        await Processor().ProcessNextAsync(TestContext.Current.CancellationToken);

        _capture.Of("freedom.elo.submission.duration").Should().ContainSingle()
            .Which.Tags["outcome"].Should().Be("error");
        _capture.Sum(
                "freedom.queue.messages.processed",
                ("queue", QueueNames.EloEnvelopes), ("outcome", "left_for_retry"))
            .Should().Be(1);
    }

    /// <summary>
    /// French customs did accept the envelope; only storing it failed. Counting that as an error
    /// would read on a dashboard as "customs is unwell", which is the wrong thing to go and look at.
    /// </summary>
    [Fact]
    public async Task A_store_that_failed_after_customs_accepted_is_not_counted_as_a_customs_error()
    {
        Accepts();
        _documents.SaveAsync(Arg.Any<EloEnvelopeDocument>(), Arg.Any<byte[]?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("storage unavailable"));
        Receives(AnItem("m-store-failed"));

        await Processor().ProcessNextAsync(TestContext.Current.CancellationToken);

        _capture.Of("freedom.elo.submission.duration").Should().ContainSingle()
            .Which.Tags["outcome"].Should().Be("accepted");
    }

    /// <summary>
    /// Linked, not parented. A message that is retried would otherwise stretch the original
    /// approval's trace across however long the retries took, and the approval finished long ago.
    /// </summary>
    [Fact]
    public async Task The_envelope_is_traced_as_a_consumer_span_linked_to_the_approval_that_queued_it()
    {
        Accepts();
        Receives(AnItem("m-link"));

        await Processor().ProcessNextAsync(TestContext.Current.CancellationToken);

        var consumer = _activities.WithTag("messaging.message.id", "m-link").Should().ContainSingle().Subject;
        consumer.Kind.Should().Be(ActivityKind.Consumer);
        consumer.Links.Should().ContainSingle()
            .Which.Context.TraceId.ToString().Should().Be("0af7651916cd43dd8448eb211c80319c");
        consumer.Parent.Should().BeNull();
    }

    [Fact]
    public async Task A_message_with_no_trace_context_is_still_processed_and_traced_unlinked()
    {
        Accepts();
        Receives(new EloWorkItem("m-unlinked", "receipt", Body(traceparent: null)));

        var processed = await Processor().ProcessNextAsync(TestContext.Current.CancellationToken);

        processed.Should().BeTrue();
        _activities.WithTag("messaging.message.id", "m-unlinked").Should().ContainSingle()
            .Which.Links.Should().BeEmpty();
    }

    [Fact]
    public async Task How_long_a_message_waited_and_whether_it_is_a_retry_are_both_reported()
    {
        Accepts();
        Receives(new EloWorkItem(
            "m-age", "receipt", Body(), DequeueCount: 3, InsertedOn: Now.AddMinutes(-4)));

        await Processor(new FixedTime(Now)).ProcessNextAsync(TestContext.Current.CancellationToken);

        _capture.Of("freedom.queue.message.age").Should().ContainSingle().Which.Value.Should().Be(240);
        _capture.Sum("freedom.queue.redeliveries", ("queue", QueueNames.EloEnvelopes)).Should().Be(1);
    }

    /// <summary>
    /// A manifest reference is fine in a log and fine on a span; as a metric tag it would make every
    /// series unique and quietly cost more than the dashboards are worth.
    /// </summary>
    [Fact]
    public async Task No_measurement_carries_a_manifest_reference_or_an_envelope_number()
    {
        Accepts();
        Receives(AnItem("m-tags"));

        await Processor().ProcessNextAsync(TestContext.Current.CancellationToken);

        var values = _capture.Measurements
            .SelectMany(measurement => measurement.Tags.Values)
            .Select(value => value?.ToString())
            .ToArray();

        values.Should().NotContain("MAN-0001").And.NotContain(NumeroDossier).And.NotContain(Jeton);
    }

    [Fact]
    public async Task The_manifest_and_message_are_on_every_log_line_written_while_it_is_processed()
    {
        Accepts();
        Receives(AnItem("m-scope"));

        await Processor().ProcessNextAsync(TestContext.Current.CancellationToken);

        var created = _logs.Sink.LogEntries
            .Should().ContainSingle(entry => entry.Message!.Contains("Created")).Subject;
        var scope = string.Join(" ", created.Scopes.Select(s => s.Message));

        scope.Should().Contain("MAN-0001").And.Contain("m-scope");
    }

    /// <summary>
    /// A poll that found nothing is not an event. Asserted through <see cref="MetricCapture"/>, which
    /// listens to this test's own <see cref="Meter"/> instance — <see cref="ActivityCapture"/> is
    /// keyed on a source name, so it sees every other test's spans too and cannot be asked whether
    /// nothing happened.
    /// </summary>
    [Fact]
    public async Task An_empty_queue_records_nothing_at_all()
    {
        _queue.ReceiveAsync(Arg.Any<CancellationToken>()).Returns((EloWorkItem?)null);

        await Processor().ProcessNextAsync(TestContext.Current.CancellationToken);

        _capture.Measurements.Should().BeEmpty();
    }

    private EloEnvelopeProcessor Processor(TimeProvider? time = null)
    {
        var tokens = Substitute.For<IEloTokenProvider>();
        tokens.GetAsync(Arg.Any<CancellationToken>()).Returns("a-token");

        return new EloEnvelopeProcessor(
            _queue,
            _elo,
            tokens,
            _documents,
            _logs.CreateLogger<EloEnvelopeProcessor>(),
            new QueueFlowMetrics(_meter, time ?? new FixedTime(Now)),
            new CustomsMetrics(_meter),
            time ?? new FixedTime(Now));
    }

    private void Receives(EloWorkItem item) =>
        _queue.ReceiveAsync(Arg.Any<CancellationToken>()).Returns(item);

    private void Accepts() =>
        _elo.CreerENVAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ENV_CRE01>(), Arg.Any<CancellationToken>())
            .Returns(new ENV_CRE02
            {
                Enveloppe = new Enveloppe
                {
                    Jeton = Jeton,
                    NumeroDossier = NumeroDossier,
                    Statut = StatutEnveloppe.FERMEE,
                    NombreDeclaration = 1,
                    Pdf = Convert.ToBase64String(Barcode),
                },
            });

    private void Refuses(int status) =>
        _elo.CreerENVAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ENV_CRE01>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new EloApiException<ENV_CRE03>(
                "Réponse KO",
                status,
                "{}",
                new Dictionary<string, IEnumerable<string>>(),
                new ENV_CRE03
                {
                    InformationsErreur = new ReponseMessageEnveloppeErreur
                    {
                        Statut = "FONC-ERR-004",
                        LibelleErreur = "Déclaration refusée.",
                    },
                },
                null));

    private static EloWorkItem AnItem(string messageId) => new(messageId, "receipt", Body());

    private static string Body(string? traceparent = Traceparent) =>
        $$"""
        {
          "manifestId": "MAN-0001",
          "crossingDirection": "Import",
          "lorryType": "Loaded",
          "tirAta": true,
          "declarationIdentifiers": [ "25FR17551780961AT5" ]
          {{(traceparent is null ? string.Empty : $", \"traceparent\": \"{traceparent}\"")}}
        }
        """;
}

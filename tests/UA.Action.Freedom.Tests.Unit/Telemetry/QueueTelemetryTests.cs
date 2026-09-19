using System.Diagnostics;
using AwesomeAssertions;
using UA.Action.Freedom.Telemetry;

namespace UA.Action.Freedom.Tests.Unit.Telemetry;

public sealed class QueueTelemetryTests : IDisposable
{
    private const string Traceparent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

    private readonly ActivityCapture _activities = new(QueueTelemetry.SourceName);

    public void Dispose() => _activities.Dispose();

    [Fact]
    public void A_consumer_span_links_to_the_span_that_enqueued_the_message_rather_than_becoming_its_child()
    {
        using (QueueTelemetry.StartConsumer("customs-work", "msg-link", Traceparent))
        {
        }

        var consumer = _activities.WithTag("messaging.message.id", "msg-link").Should().ContainSingle().Subject;

        consumer.Kind.Should().Be(ActivityKind.Consumer);
        consumer.ParentId.Should().BeNull();
        consumer.Links.Should().ContainSingle()
            .Which.Context.TraceId.ToString().Should().Be("4bf92f3577b34da6a3ce929d0e0e4736");
    }

    [Fact]
    public void A_consumer_span_describes_the_queue_and_message_without_the_body()
    {
        using (QueueTelemetry.StartConsumer("manifest-documents", "msg-tags", Traceparent))
        {
        }

        var consumer = _activities.WithTag("messaging.message.id", "msg-tags").Single();

        consumer.GetTagItem("messaging.system").Should().Be("azure_storage_queue");
        consumer.GetTagItem("messaging.destination.name").Should().Be("manifest-documents");
        consumer.GetTagItem("messaging.operation.type").Should().Be("process");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-traceparent")]
    public void A_message_with_no_usable_trace_context_starts_an_unlinked_consumer_span(string? traceparent)
    {
        using (QueueTelemetry.StartConsumer("customs-work", $"msg-none-{traceparent?.Length}", traceparent))
        {
        }

        _activities.WithTag("messaging.message.id", $"msg-none-{traceparent?.Length}")
            .Should().ContainSingle().Which.Links.Should().BeEmpty();
    }

    [Fact]
    public void A_producer_span_names_the_queue_it_publishes_to()
    {
        using (var producer = QueueTelemetry.StartProducer("customs-work"))
        {
            producer.Should().NotBeNull();
        }

        var span = _activities.WithTag("messaging.destination.name", "customs-work")
            .Single(a => a.Kind == ActivityKind.Producer);

        span.GetTagItem("messaging.operation.type").Should().Be("publish");
    }

    [Fact]
    public void The_current_traceparent_is_the_active_spans_id()
    {
        using var producer = QueueTelemetry.StartProducer("customs-work");

        QueueTelemetry.CurrentTraceparent().Should().Be(producer!.Id);
    }

    [Fact]
    public void There_is_no_traceparent_to_send_when_nothing_is_being_traced()
    {
        Activity.Current = null;

        QueueTelemetry.CurrentTraceparent().Should().BeNull();
    }

    private sealed record Payload(string ManifestId, int Weight, string? Note);

    [Fact]
    public void A_message_is_written_as_the_camelCase_json_the_workers_read()
    {
        var body = QueueTelemetry.Serialize(new Payload("MAN-1", 40, null), traceparent: null);

        body.Should().Be("""{"manifestId":"MAN-1","weight":40,"note":null}""");
    }

    [Fact]
    public void A_traceparent_is_added_beside_the_payload_without_disturbing_it()
    {
        var body = QueueTelemetry.Serialize(new Payload("MAN-1", 40, "x"), Traceparent);

        body.Should().Be(
            $$"""{"manifestId":"MAN-1","weight":40,"note":"x","traceparent":"{{Traceparent}}"}""");
    }

    [Fact]
    public void A_message_sent_with_no_trace_context_carries_no_traceparent_property_at_all()
    {
        QueueTelemetry.Serialize(new Payload("MAN-1", 40, null), traceparent: null)
            .Should().NotContain("traceparent");
    }

    [Fact]
    public void The_traceparent_survives_being_read_back_as_the_workers_read_it()
    {
        var body = QueueTelemetry.Serialize(new Payload("MAN-1", 40, null), Traceparent);

        var read = System.Text.Json.JsonSerializer.Deserialize<WorkerView>(
            body, System.Text.Json.JsonSerializerOptions.Web);

        read!.Traceparent.Should().Be(Traceparent);
        read.ManifestId.Should().Be("MAN-1");
    }

    private sealed record WorkerView(string ManifestId, string? Traceparent = null);
}

using System.Diagnostics;
using System.Text.Json;

namespace UA.Action.Freedom.Telemetry;

/// <summary>
/// Spans for the two queues, and the W3C trace context that lets one trace survive the hand-off.
/// </summary>
/// <remarks>
/// Queue Storage has no message headers, so the producer writes <c>traceparent</c> into the JSON
/// body next to the payload and the consumer reads it back. The consumer <em>links</em> to it
/// rather than becoming its child: a message that is retried is processed minutes after the
/// request that produced it, and a child span would stretch the request's trace across that gap.
/// A link keeps the two traces separate and still navigable in Tempo.
/// </remarks>
public static class QueueTelemetry
{
    public const string SourceName = "UA.Action.Freedom.Queue";

    private const string MessagingSystem = "azure_storage_queue";

    public static readonly ActivitySource Source = new(SourceName);

    /// <summary>The producer span, a child of whatever request is enqueueing.</summary>
    public static Activity? StartProducer(string queue)
    {
        var activity = Source.StartActivity($"publish {queue}", ActivityKind.Producer);

        activity?.SetTag("messaging.system", MessagingSystem);
        activity?.SetTag("messaging.destination.name", queue);
        activity?.SetTag("messaging.operation.type", "publish");

        return activity;
    }

    /// <summary>
    /// The consumer span for one message. Only identifiers are recorded — never the body, which
    /// may carry a manifest.
    /// </summary>
    public static Activity? StartConsumer(string queue, string messageId, string? traceparent)
    {
        var producer = ActivityContext.TryParse(traceparent, null, out var context)
            ? new[] { new ActivityLink(context) }
            : [];

        return Source.StartActivity(
            $"process {queue}",
            ActivityKind.Consumer,
            default(ActivityContext),
            tags:
            [
                new("messaging.system", MessagingSystem),
                new("messaging.destination.name", queue),
                new("messaging.operation.type", "process"),
                new("messaging.message.id", messageId),
            ],
            links: producer);
    }

    /// <summary>The value to put in a message's <c>traceparent</c>, or null when nothing is being traced.</summary>
    public static string? CurrentTraceparent() =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C } current ? current.Id : null;

    /// <summary>
    /// Writes a queue message as the camelCase JSON the workers read (<see cref="JsonSerializerOptions.Web"/>),
    /// with <paramref name="traceparent"/> beside the payload. The property is left out entirely
    /// when there is nothing to send, so an untraced message is byte-for-byte the message it was
    /// before tracing existed.
    /// </summary>
    public static string Serialize(object payload, string? traceparent)
    {
        var message = JsonSerializer.SerializeToNode(payload, JsonSerializerOptions.Web)!.AsObject();

        if (traceparent is not null)
        {
            message["traceparent"] = traceparent;
        }

        return message.ToJsonString(JsonSerializerOptions.Web);
    }
}

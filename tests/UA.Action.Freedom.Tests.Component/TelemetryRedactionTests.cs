using System.Diagnostics;
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Application.Vehicles;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// What the application tells the telemetry backend about a request, and — more to the point —
/// what it does not. Traces and logs are retained and read by more people than the data they
/// describe, so anything identifying a person, a receiver or a capability must never reach them.
/// </summary>
/// <remarks>
/// Every test sends its own <c>traceparent</c> and reads back only that trace. Tracer providers
/// listen to <see cref="ActivitySource"/>s process-wide, so a test host also sees the spans of every
/// other host running in parallel; scoping by trace is what keeps these assertions about
/// <em>this</em> request.
/// </remarks>
public class TelemetryRedactionTests
{
    private static readonly Guid Ref = new("b3f1c4d2-5a6e-4f70-8901-2c3d4e5f6a7b");

    private const string Reason = "delivering to Dr Petrenko in Kharkiv";

    private sealed record Exported(List<Activity> Spans, List<Metric> Metrics)
    {
        public List<Activity> In(ActivityTraceId trace) => [.. Spans.Where(span => span.TraceId == trace)];
    }

    private static (WebApplicationFactory<Program> Api, Exported Exported) Observe(
        WebApplicationFactory<Program> api)
    {
        var exported = new Exported([], []);

        var observed = api.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddOpenTelemetry()
                .WithTracing(tracing => tracing.AddInMemoryExporter(exported.Spans))
                .WithMetrics(metrics => metrics.AddInMemoryExporter(exported.Metrics))));

        return (observed, exported);
    }

    private static HttpClient ClientTracedAs(WebApplicationFactory<Program> api, ActivityTraceId trace)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add("traceparent", $"00-{trace}-{ActivitySpanId.CreateRandom()}-01");
        return client;
    }

    /// <summary>
    /// Flushes, and waits for <paramref name="trace"/> to have a server span. The response reaches
    /// the client slightly before ASP.NET Core stops the request's span, so reading straight after
    /// the response is a race.
    /// </summary>
    private static async Task FlushAsync(
        WebApplicationFactory<Program> api, Exported exported, ActivityTraceId? trace = null)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            api.Services.GetRequiredService<TracerProvider>().ForceFlush();
            api.Services.GetRequiredService<MeterProvider>().ForceFlush();

            if (trace is null || exported.In(trace.Value).Any(span => span.Kind == ActivityKind.Server))
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task A_receiver_address_lookup_leaves_neither_the_reason_nor_the_receiver_reference_in_the_trace()
    {
        var trace = ActivityTraceId.CreateRandom();
        var (api, exported) = Observe(FreedomApi.WithReceivers(
            new InMemoryReceiverRepository(new ReceiverReadModel(Ref, "Kharkiv Regional Hospital", "Kharkiv oblast")),
            new InMemoryReceiverDetailRepository(
                new ReceiverDetailReadModel(Ref, "Olena Kovalenko", "+380501234567", "12 Vulytsia Sumska", null, "Kharkiv", "61002", null)),
            roles: "GroundOfficer"));
        await using var _ = api;
        using var client = ClientTracedAs(api, trace);

        await client.GetAsync(
            $"/receivers/{Ref}/detail?reason={Uri.EscapeDataString(Reason)}", TestContext.Current.CancellationToken);
        await FlushAsync(api, exported, trace);

        var spans = exported.In(trace);
        var server = spans.Should().ContainSingle(span => span.Kind == ActivityKind.Server).Subject;

        server.GetTagItem("url.query").Should().BeNull();

        var everythingRecorded = spans
            .SelectMany(span => span.TagObjects.Select(tag => tag.Value?.ToString() ?? string.Empty)
                .Append(span.DisplayName))
            .ToList();

        everythingRecorded.Should().NotContain(value => value.Contains("Petrenko"));
        everythingRecorded.Should().NotContain(value => value.Contains(Ref.ToString()));
    }

    [Fact]
    public async Task Health_probes_are_not_traced()
    {
        var probe = ActivityTraceId.CreateRandom();
        var real = ActivityTraceId.CreateRandom();
        var (api, exported) = Observe(FreedomApi.WithNoBackingServices());
        await using var _ = api;

        using (var client = ClientTracedAs(api, probe))
        {
            await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        }

        using (var client = ClientTracedAs(api, real))
        {
            await client.GetAsync("/vehicles", TestContext.Current.CancellationToken);
        }

        await FlushAsync(api, exported, real);

        exported.In(real).Should().Contain(span => span.Kind == ActivityKind.Server);
        exported.In(probe).Should().BeEmpty();
    }

    [Fact]
    public async Task Health_probes_are_not_counted_in_the_request_metrics()
    {
        var (api, exported) = Observe(FreedomApi.WithNoBackingServices());
        await using var _ = api;
        using var client = api.CreateClient();

        await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        await client.GetAsync("/vehicles", TestContext.Current.CancellationToken);
        await FlushAsync(api, exported);

        var routes = exported.Metrics
            .Where(metric => metric.Name == "http.server.request.duration")
            .SelectMany(metric => RouteTags(metric))
            .ToList();

        routes.Should().Contain(route => route.Contains("vehicles"));
        routes.Should().NotContain(route => route.Contains("health"));
    }

    private static IEnumerable<string> RouteTags(Metric metric)
    {
        foreach (var point in metric.GetMetricPoints())
        {
            foreach (var tag in point.Tags)
            {
                if (tag.Key == "http.route" && tag.Value is string route)
                {
                    yield return route;
                }
            }
        }
    }

    [Fact]
    public async Task An_unhandled_failure_returns_the_trace_id_and_none_of_the_cause()
    {
        var trace = ActivityTraceId.CreateRandom();
        var vehicles = Substitute.For<IVehicleRepository>();
        vehicles.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Login failed for user 'freedom_app' on sql-prod-01"));

        var (api, exported) = Observe(FreedomApi.WithVehicles(vehicles, roles: "Administrator"));
        await using var _ = api;
        using var client = ClientTracedAs(api, trace);

        var response = await client.GetAsync("/vehicles", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        await FlushAsync(api, exported, trace);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        body.Should().NotContain("freedom_app").And.NotContain("sql-prod-01");

        var traceId = JsonDocument.Parse(body).RootElement.GetProperty("traceId").GetString();
        traceId.Should().Be(trace.ToString());
        exported.In(trace).Should().Contain(span => span.Kind == ActivityKind.Server);
    }
}

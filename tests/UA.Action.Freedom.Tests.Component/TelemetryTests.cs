using System.Diagnostics;
using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// The application has to describe its own behaviour to the outside world — that is the
/// whole point of routing telemetry to Grafana / Application Insights. If an ordinary
/// request produces no server span then nothing downstream has anything to read, however
/// well the collector is configured.
/// </summary>
public class TelemetryTests
{
    private static bool ServerSpanForVehicles(List<Activity> spans) =>
        spans.ToArray().Any(span => span.Kind == ActivityKind.Server && span.DisplayName.Contains("vehicles"));

    [Fact]
    public async Task An_incoming_request_is_traced_as_a_server_span()
    {
        var exportedSpans = new List<Activity>();

        await using var api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Hosting:UseHttpsRedirection", "false");
            builder.ConfigureServices(services =>
                services.AddOpenTelemetry().WithTracing(tracing =>
                    tracing.AddInMemoryExporter(exportedSpans)));
        });
        using var client = api.CreateClient();

        // A real business route rather than a health probe: probes are the first thing anyone
        // filters out of tracing, and this test is about ordinary traffic. No token is sent, so
        // this is the 401 path — which still has to be traced, or an authorization problem in
        // production is invisible.
        await client.GetAsync("/vehicles", TestContext.Current.CancellationToken);

        // The response reaches the client slightly before ASP.NET Core stops the request's span,
        // so wait for the span rather than racing it.
        for (var attempt = 0; attempt < 50 && !ServerSpanForVehicles(exportedSpans); attempt++)
        {
            api.Services.GetRequiredService<TracerProvider>().ForceFlush();
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        ServerSpanForVehicles(exportedSpans).Should().BeTrue();
    }

    [Fact]
    public async Task A_refused_command_is_counted_by_handler_and_outcome()
    {
        var exportedMetrics = new List<Metric>();

        await using var api = FreedomApi.WithVehicles(new InMemoryVehicleRepository(), roles: "Administrator")
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddInMemoryExporter(exportedMetrics))));
        using var client = api.CreateClient();

        var response = await client.DeleteAsync("/vehicles/NO-SUCH-VIN", TestContext.Current.CancellationToken);
        api.Services.GetRequiredService<MeterProvider>().ForceFlush();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        exportedMetrics
            .Where(metric => metric.Name == "freedom.handler.invocations")
            .SelectMany(metric => TagSets(metric))
            .Should().Contain(tags =>
                tags["handler"] == "DeleteVehicle" && tags["outcome"] == "NotFound" && tags["result"] == "rejected");
    }

    private static IEnumerable<Dictionary<string, string?>> TagSets(Metric metric)
    {
        foreach (var point in metric.GetMetricPoints())
        {
            var tags = new Dictionary<string, string?>();

            foreach (var tag in point.Tags)
            {
                tags[tag.Key] = tag.Value?.ToString();
            }

            yield return tags;
        }
    }
}

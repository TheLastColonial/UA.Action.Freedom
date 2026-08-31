using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
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
        api.Services.GetRequiredService<TracerProvider>().ForceFlush();

        exportedSpans.Should().Contain(span =>
            span.Kind == ActivityKind.Server && span.DisplayName.Contains("vehicles"));
    }
}

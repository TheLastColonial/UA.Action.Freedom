using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace UA.Action.Freedom.Api.Installer;

/// <summary>
/// Routes the application's traces, metrics and logs to an OpenTelemetry collector — the
/// Grafana OTEL-LGTM container in the local Azure simulation (<c>iac/local</c>), and
/// Application Insights' OTLP ingest in the target design (<c>docs/recommendations.md</c>,
/// <c>docs/c4/2-containers.puml</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Where</b> telemetry goes is read by the SDK from the standard OTLP environment
/// variables — <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, <c>OTEL_EXPORTER_OTLP_PROTOCOL</c>,
/// <c>OTEL_SERVICE_NAME</c>, <c>OTEL_RESOURCE_ATTRIBUTES</c> — not from an options section,
/// keeping to the rule that configuration comes from the environment and nothing else. The
/// local environment sets them for the <c>app</c> service in
/// <c>iac/local/docker-compose.yml</c> (endpoint <c>http://telemetry:4317</c>, gRPC).
/// </para>
/// <para>
/// When <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is unset there is no collector to ship to, so
/// the OTLP exporter is left unregistered rather than left retrying a refused connection
/// on a loop. That is the bare <c>dotnet run</c> case and the in-memory component tests —
/// instrumentation is still collected in-process, just not exported.
/// </para>
/// </remarks>
public static class TelemetryInstaller
{
    private const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    public static IHostApplicationBuilder AddFreedomTelemetry(this IHostApplicationBuilder builder)
    {
        var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")
                          ?? builder.Environment.ApplicationName;

        var telemetry = builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: serviceName,
                serviceVersion: typeof(TelemetryInstaller).Assembly.GetName().Version?.ToString(),
                serviceInstanceId: Environment.MachineName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;
        });

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OtlpEndpointVariable)))
        {
            // Cross-cuts every signal — traces, metrics and the logging provider above —
            // and reads endpoint, protocol and headers from the OTEL_EXPORTER_OTLP_*
            // environment variables.
            telemetry.UseOtlpExporter();
        }

        return builder;
    }
}

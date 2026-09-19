using System.Diagnostics.Metrics;
using System.Reflection;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace UA.Action.Freedom.Telemetry;

/// <summary>
/// Routes a service's traces, metrics and logs to an OpenTelemetry collector — the Grafana
/// OTEL-LGTM container in the local Azure simulation (<c>iac/local</c>), and the collector or
/// Application Insights ingest in the target design (<c>docs/recommendations.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// Shared by the Api and both workers so they emit the same resource, the same redaction and the
/// same custom sources. <b>Where</b> telemetry goes is read by the SDK from the standard
/// <c>OTEL_EXPORTER_OTLP_*</c>, <c>OTEL_SERVICE_NAME</c>, <c>OTEL_RESOURCE_ATTRIBUTES</c> and
/// <c>OTEL_TRACES_SAMPLER</c> variables — configuration comes from the environment and nothing
/// else. With no endpoint set the exporter is left unregistered rather than retrying a refused
/// connection on a loop; instrumentation is still collected in-process.
/// </para>
/// <para>
/// <b>Do not set <c>service.namespace</c>.</b> The OTLP-to-Prometheus mapping turns it into a
/// prefix of the <c>job</c> label (<c>freedom/freedom-app</c>), which every dashboard filters on.
/// </para>
/// </remarks>
public static class FreedomTelemetry
{
    /// <summary>Matches every <see cref="ActivitySource"/> and <see cref="Meter"/> the solution defines.</summary>
    public const string SourcePattern = "UA.Action.Freedom.*";

    private const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string ResourceAttributesVariable = "OTEL_RESOURCE_ATTRIBUTES";
    private const string EnvironmentAttribute = "deployment.environment.name";

    public static IHostApplicationBuilder AddFreedomTelemetry(
        this IHostApplicationBuilder builder,
        Action<TracerProviderBuilder>? configureTracing = null,
        Action<MeterProviderBuilder>? configureMetrics = null)
    {
        // Makes the Azure SDK emit a span per operation. Must be set before the first client is
        // created.
        AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

        var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")
                          ?? builder.Environment.ApplicationName;

        var telemetry = builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                resource.AddService(
                    serviceName: serviceName,
                    serviceVersion: ServiceVersion(Assembly.GetEntryAssembly()),
                    serviceInstanceId: Environment.MachineName);

                if (Environment.GetEnvironmentVariable(ResourceAttributesVariable)?.Contains(EnvironmentAttribute) != true)
                {
                    resource.AddAttributes([new(EnvironmentAttribute, builder.Environment.EnvironmentName.ToLowerInvariant())]);
                }
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(SourcePattern)
                    // One span per queue or blob operation (QueueClient.DeleteMessage). Not
                    // Azure.Core.Http, which would repeat every HTTP call the HttpClient
                    // instrumentation already records.
                    .AddSource("Azure.Storage.*")
                    .AddHttpClientInstrumentation();

                configureTracing?.Invoke(tracing);

                // After every instrumentation and before the exporter, so the exporter sees the
                // redacted span.
                tracing.AddProcessor(new RedactingActivityProcessor());
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(SourcePattern)
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                configureMetrics?.Invoke(metrics);
            });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;
        });

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OtlpEndpointVariable)))
        {
            // Cross-cuts every signal — traces, metrics and the logging provider above.
            telemetry.UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>Queue counters, for a service that only enqueues (the Api).</summary>
    public static IServiceCollection AddFreedomQueueFlowMetrics(this IServiceCollection services) =>
        services.AddSingleton(provider => QueueFlowMetrics.Create(provider.GetRequiredService<IMeterFactory>()));

    /// <summary>
    /// Everything a queue-draining worker reports: message counters, loop heartbeats, and the
    /// depth of its work and poison queues. Needs a <see cref="QueueServiceClient"/> registered.
    /// </summary>
    public static IServiceCollection AddFreedomWorkerTelemetry(
        this IServiceCollection services, params WatchedQueue[] queues)
    {
        services.AddFreedomQueueFlowMetrics();
        services.AddSingleton(provider => WorkerLoopMetrics.Create(provider.GetRequiredService<IMeterFactory>()));
        services.AddSingleton<IQueueDepthProbe>(provider =>
            new AzureQueueDepthProbe(provider.GetRequiredService<QueueServiceClient>()));
        services.AddHostedService(provider => new QueueDepthReporter(
            provider.GetRequiredService<IQueueDepthProbe>(),
            queues,
            provider.GetRequiredService<IMeterFactory>().Create(QueueDepthReporter.MeterName),
            TimeProvider.System,
            TimeSpan.FromSeconds(30),
            provider.GetRequiredService<ILogger<QueueDepthReporter>>()));

        return services;
    }

    private static string? ServiceVersion(Assembly? assembly)
    {
        var informational = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return informational?.Split('+')[0] ?? assembly?.GetName().Version?.ToString();
    }
}

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using UA.Action.Freedom.Telemetry;

namespace UA.Action.Freedom.Api.Installer;

/// <summary>
/// The Api's half of the telemetry wiring: what only a web host and a SQL client have.
/// Everything shared with the workers — resource, redaction, exporter, custom sources — is in
/// <see cref="FreedomTelemetry"/>.
/// </summary>
public static class TelemetryInstaller
{
    /// <summary>The probes Container Apps, docker compose and Traefik call every few seconds.</summary>
    private static readonly PathString Probes = new("/health");

    public static IHostApplicationBuilder AddFreedomApiTelemetry(this IHostApplicationBuilder builder) =>
        builder.AddFreedomTelemetry(
            tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                    // A probe every ten seconds would out-number real traffic in every RED panel
                    // and spend the free-tier ingestion allowance on nothing.
                    options.Filter = context => !context.Request.Path.StartsWithSegments(Probes))
                .AddSqlClientInstrumentation(),
            metrics => metrics.AddAspNetCoreInstrumentation());
}

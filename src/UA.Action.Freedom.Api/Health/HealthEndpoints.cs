using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UA.Action.Freedom.Api.Health;

/// <summary>
/// Wires up the two probes Container Apps and docker compose both expect.
/// </summary>
public static class HealthEndpoints
{
    /// <summary>Tag marking a check as part of readiness rather than liveness.</summary>
    public const string ReadyTag = "ready";

    /// <summary>
    /// The ceiling on any single readiness check. Whatever the SDK underneath decides to
    /// do, the probe answers within roughly this long — a health endpoint that hangs is
    /// indistinguishable from a dead application to the thing polling it.
    /// </summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(8);

    public static IServiceCollection AddFreedomHealthChecks(this IServiceCollection services)
    {
        services.AddHttpClient(IdentityProviderHealthCheck.Name,
            client => client.Timeout = TimeSpan.FromSeconds(5));

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>(DatabaseHealthCheck.Name, tags: [ReadyTag], timeout: CheckTimeout)
            .AddCheck<DocumentStoreHealthCheck>(DocumentStoreHealthCheck.Name, tags: [ReadyTag], timeout: CheckTimeout)
            .AddCheck<CustomsQueueHealthCheck>(CustomsQueueHealthCheck.Name, tags: [ReadyTag], timeout: CheckTimeout)
            .AddCheck<IdentityProviderHealthCheck>(IdentityProviderHealthCheck.Name, tags: [ReadyTag], timeout: CheckTimeout);

        return services;
    }

    public static WebApplication MapFreedomHealthChecks(this WebApplication app)
    {
        // Liveness answers only "is this process worth keeping". It runs no checks at all,
        // so a database still waking from auto-pause cannot get the container killed and
        // restarted into the same wait.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag),
            ResponseWriter = WriteReport,
        });

        return app;
    }

    private static async Task WriteReport(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            durationMs = (int)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = (int)entry.Value.Duration.TotalMilliseconds,
                // The description carries the "why" — a missing container reads very
                // differently from an unreachable account, and whoever is debugging at
                // 6am on loading day should not have to go to the logs to tell them apart.
                description = entry.Value.Description ?? entry.Value.Exception?.Message,
            }),
        };

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }
}

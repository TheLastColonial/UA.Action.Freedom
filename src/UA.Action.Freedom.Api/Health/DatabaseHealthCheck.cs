using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UA.Action.Freedom.Api.Health;

/// <summary>
/// Opens a connection to the Freedom database and runs the cheapest possible query.
/// </summary>
/// <remarks>
/// This check doubles as the auto-pause alarm. The Azure SQL free offer pauses after an
/// idle period and the first connection afterwards can take tens of seconds
/// (<c>docs/recommendations.md</c> §2.3), so a readiness probe that fails here is usually
/// reporting a waking database rather than a broken one — which is exactly why it belongs
/// on readiness and not on liveness.
/// </remarks>
public sealed class DatabaseHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public const string Name = "database";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("Freedom");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Unhealthy("No Freedom connection string is configured.");
        }

        // A probe waits far less patiently than a request does. Whatever timeout the
        // deployed connection string carries, cap it here so readiness answers in time to
        // be useful.
        var probeConnectionString = new SqlConnectionStringBuilder(connectionString)
        {
            ConnectTimeout = 5,
        }.ConnectionString;

        try
        {
            await using var connection = new SqlConnection(probeConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy($"Connected to {connection.Database}.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Could not reach the Freedom database.", exception);
        }
    }
}

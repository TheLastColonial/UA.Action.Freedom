using Azure.Storage.Queues;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.Api.Configuration;

namespace UA.Action.Freedom.Api.Health;

/// <summary>
/// Confirms the French logistics envelope queue exists and is reachable.
/// </summary>
/// <remarks>
/// The same silent failure as <see cref="CustomsQueueHealthCheck"/>, with a worse ending. Approving a
/// manifest is the point of no return: if this queue is missing, the manifest freezes, the approval
/// reports the failure once, and a vehicle reaches the French Smart Border with no envelope — which
/// is where it stops. Worth failing readiness over rather than discovering at Dover.
/// </remarks>
public sealed class EloQueueHealthCheck(
    IOptions<StorageOptions> options,
    QueueServiceClient? queues = null) : IHealthCheck
{
    public const string Name = "elo-queue";

    private readonly StorageOptions _storage = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (queues is null)
        {
            return HealthCheckResult.Unhealthy("No storage account is configured.");
        }

        try
        {
            var queue = queues.GetQueueClient(_storage.EloQueue);

            return await queue.ExistsAsync(cancellationToken)
                ? HealthCheckResult.Healthy($"Queue '{_storage.EloQueue}' is reachable.")
                : HealthCheckResult.Unhealthy(
                    $"Queue '{_storage.EloQueue}' does not exist. Has `tofu apply` run?");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Could not reach the French logistics envelope queue.", exception);
        }
    }
}

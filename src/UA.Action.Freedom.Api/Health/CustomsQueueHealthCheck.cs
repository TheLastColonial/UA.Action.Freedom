using Azure.Storage.Queues;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.Api.Configuration;

namespace UA.Action.Freedom.Api.Health;

/// <summary>
/// Confirms the customs work queue exists and is reachable.
/// </summary>
/// <remarks>
/// The queue is the durable hand-off to the Customs Worker. If it is missing, a dispatcher
/// requesting a GMR gets a success response and nothing ever happens — a silent failure
/// worth failing readiness over.
/// </remarks>
public sealed class CustomsQueueHealthCheck(
    IOptions<StorageOptions> options,
    QueueServiceClient? queues = null) : IHealthCheck
{
    public const string Name = "customs-queue";

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
            var queue = queues.GetQueueClient(_storage.CustomsQueue);

            return await queue.ExistsAsync(cancellationToken)
                ? HealthCheckResult.Healthy($"Queue '{_storage.CustomsQueue}' is reachable.")
                : HealthCheckResult.Unhealthy(
                    $"Queue '{_storage.CustomsQueue}' does not exist. Has `tofu apply` run?");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Could not reach the customs work queue.", exception);
        }
    }
}

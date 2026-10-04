using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.Api.Configuration;

namespace UA.Action.Freedom.Api.Health;

/// <summary>
/// Confirms the container holding recorded ICS2 declarations exists and is reachable.
/// </summary>
/// <remarks>
/// Worth its own check rather than folding into <see cref="DocumentStoreHealthCheck"/>, because this
/// is the one container the API <em>writes</em>. A missing one does not degrade a read: it stops a
/// dispatcher recording an ENS, which stops its vehicle's ELO being filed, which stops
/// the crossing. Better to learn that from <c>/health/ready</c> than from the first 500 on the day of a
/// crossing.
/// </remarks>
public sealed class EnsStoreHealthCheck(
    IOptions<StorageOptions> options,
    BlobServiceClient? blobs = null) : IHealthCheck
{
    public const string Name = "ens-store";

    private readonly StorageOptions _storage = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (blobs is null)
        {
            return HealthCheckResult.Unhealthy("No storage account is configured.");
        }

        try
        {
            var container = blobs.GetBlobContainerClient(_storage.EnsContainer);

            return await container.ExistsAsync(cancellationToken)
                ? HealthCheckResult.Healthy($"Container '{_storage.EnsContainer}' is reachable.")
                : HealthCheckResult.Unhealthy(
                    $"Container '{_storage.EnsContainer}' does not exist, so no ICS2 declaration can be "
                    + "recorded and no ELO can be filed. Has `tofu apply` run?");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Could not reach the ICS2 declaration store.", exception);
        }
    }
}

using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.Api.Configuration;

namespace UA.Action.Freedom.Api.Health;

/// <summary>
/// Confirms the documents container exists and is reachable.
/// </summary>
/// <remarks>
/// Checks the container rather than the account, because the account being up says nothing
/// about whether provisioning has run. A green account with a missing container is the
/// failure this catches: uploads would fail at the first manifest, not at startup.
/// </remarks>
public sealed class DocumentStoreHealthCheck(
    IOptions<StorageOptions> options,
    BlobServiceClient? blobs = null) : IHealthCheck
{
    public const string Name = "documents";

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
            var container = blobs.GetBlobContainerClient(_storage.DocumentsContainer);

            return await container.ExistsAsync(cancellationToken)
                ? HealthCheckResult.Healthy($"Container '{_storage.DocumentsContainer}' is reachable.")
                : HealthCheckResult.Unhealthy(
                    $"Container '{_storage.DocumentsContainer}' does not exist. Has `tofu apply` run?");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Could not reach the document store.", exception);
        }
    }
}

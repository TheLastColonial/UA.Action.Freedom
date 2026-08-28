using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

namespace UA.Action.Freedom.Api.Configuration;

/// <summary>
/// Registers the Blob and Queue clients the application talks to the storage account with.
/// </summary>
public static class StorageExtensions
{
    /// <summary>
    /// How hard to try before treating a storage endpoint as unreachable.
    /// </summary>
    /// <remarks>
    /// The SDK default is a long exponential backoff, which is right for a request a user
    /// is waiting on and wrong here, for two reasons.
    /// <para>
    /// A health probe that takes minutes to fail never reports failure at all — the
    /// orchestrator polling it gives up in single-digit seconds, so the only answers it
    /// ever sees are "healthy" and "timed out".
    /// </para>
    /// <para>
    /// More importantly the data-protection key ring is read during startup (§3.2), so the
    /// retry budget is also the cold-start budget. On the default policy an unreachable
    /// storage account stalls startup for the better part of a minute, and with
    /// <c>minReplicas: 0</c> a cold start is the normal case rather than the exception.
    /// </para>
    /// </remarks>
    private static void ConfigureRetry(RetryOptions retry)
    {
        retry.MaxRetries = 1;
        retry.NetworkTimeout = TimeSpan.FromSeconds(5);
        retry.Delay = TimeSpan.FromMilliseconds(200);
        retry.MaxDelay = TimeSpan.FromSeconds(1);
    }

    public static BlobServiceClient CreateBlobServiceClient(StorageOptions storage)
    {
        var options = new BlobClientOptions();
        ConfigureRetry(options.Retry);

        return new BlobServiceClient(storage.ConnectionString, options);
    }

    public static QueueServiceClient CreateQueueServiceClient(StorageOptions storage)
    {
        var options = new QueueClientOptions();
        ConfigureRetry(options.Retry);

        return new QueueServiceClient(storage.ConnectionString, options);
    }

    /// <summary>
    /// Registers the storage clients, but only when a storage account is actually
    /// configured. Health checks take them as optional dependencies and report the absence
    /// rather than failing to construct, so an unconfigured application still starts and
    /// still explains itself on <c>/health/ready</c>.
    /// </summary>
    public static IServiceCollection AddFreedomStorage(this IServiceCollection services, StorageOptions storage)
    {
        if (!storage.IsConfigured)
        {
            return services;
        }

        services.AddSingleton(_ => CreateBlobServiceClient(storage));
        services.AddSingleton(_ => CreateQueueServiceClient(storage));

        return services;
    }
}

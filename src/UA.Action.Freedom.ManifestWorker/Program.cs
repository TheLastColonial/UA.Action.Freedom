using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UA.Action.Freedom.ManifestWorker;
using UA.Action.Freedom.ManifestWorker.Configuration;
using UA.Action.Freedom.ManifestWorker.Documents;
using UA.Action.Freedom.ManifestWorker.Queueing;

var builder = Host.CreateApplicationBuilder(args);

// Configuration comes from the environment and nothing else, like the rest of the solution.
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));

var storage = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
              ?? new StorageOptions();

if (!storage.IsConfigured)
{
    // Fail fast, and say why it matters rather than null-referencing three layers down.
    throw new InvalidOperationException(
        "Storage:ConnectionString is required. The worker has nothing to do without a queue to read "
        + "and nowhere to put the documents it renders.");
}

// Bounded retries: the default exponential backoff turns a brief storage outage into a worker
// that appears hung rather than one that logs and tries again on the next tick.
static void ConfigureRetry(RetryOptions retry)
{
    retry.MaxRetries = 1;
    retry.NetworkTimeout = TimeSpan.FromSeconds(5);
    retry.Delay = TimeSpan.FromMilliseconds(200);
    retry.MaxDelay = TimeSpan.FromSeconds(1);
}

builder.Services.AddSingleton(_ =>
{
    var options = new QueueClientOptions();
    ConfigureRetry(options.Retry);
    return new QueueServiceClient(storage.ConnectionString, options);
});

builder.Services.AddSingleton(_ =>
{
    var options = new BlobClientOptions();
    ConfigureRetry(options.Retry);
    return new BlobServiceClient(storage.ConnectionString, options);
});

builder.Services.AddSingleton<IManifestDocumentQueue, AzureManifestDocumentQueue>();
builder.Services.AddSingleton<IManifestDocumentStore, BlobManifestDocumentStore>();

// Registered by concrete type: only the hosted service consumes it.
builder.Services.AddSingleton<ManifestDocumentProcessor>();

builder.Services.AddHostedService<ManifestWorkerService>();

await builder.Build().RunAsync();

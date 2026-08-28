using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using HMRC.GVMS;
using HMRC.PushPullNotifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.CustomsWorker;
using UA.Action.Freedom.CustomsWorker.Configuration;
using UA.Action.Freedom.CustomsWorker.Customs;
using UA.Action.Freedom.CustomsWorker.Queueing;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.Configure<HmrcOptions>(builder.Configuration.GetSection(HmrcOptions.SectionName));

var storage = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
              ?? new StorageOptions();
var hmrc = builder.Configuration.GetSection(HmrcOptions.SectionName).Get<HmrcOptions>()
           ?? new HmrcOptions();

if (!storage.IsConfigured)
{
    throw new InvalidOperationException(
        "Storage:ConnectionString is required. The worker has nothing to do without a queue to read.");
}

// Bounded retries, for the same reason as in the Api: the default exponential backoff turns
// a brief storage outage into a worker that appears hung rather than one that logs and
// tries again on the next tick.
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

// The HMRC SDKs are reused exactly as they ship: only the base URL changes, which is what
// points them at WireMock locally and at HMRC's sandbox or production elsewhere.
// Authentication is the caller's job by design — attach the OAuth handler here when the
// client credentials are real.
builder.Services.AddGvmsClient(options =>
{
    if (!string.IsNullOrWhiteSpace(hmrc.Gvms.BaseUrl))
    {
        options.BaseUrl = new Uri(hmrc.Gvms.BaseUrl);
    }
});

builder.Services.AddPushPullNotificationsClient(options =>
{
    if (!string.IsNullOrWhiteSpace(hmrc.Ppns.BaseUrl))
    {
        options.BaseUrl = new Uri(hmrc.Ppns.BaseUrl);
    }
});

builder.Services.AddSingleton<ICustomsWorkQueue, AzureCustomsWorkQueue>();
builder.Services.AddSingleton<IGmrDocumentStore, BlobGmrDocumentStore>();

builder.Services.AddSingleton<GmrSubmissionProcessor>();
builder.Services.AddSingleton(provider => new GmrOutcomeCollector(
    provider.GetRequiredService<IPushPullNotificationsClient>(),
    provider.GetRequiredService<IGmrDocumentStore>(),
    provider.GetRequiredService<IOptions<HmrcOptions>>().Value.Ppns.BoxId
        ?? throw new InvalidOperationException(
            "Hmrc:Ppns:BoxId is required. Without a box there is nowhere to collect GMR outcomes from."),
    provider.GetRequiredService<ILogger<GmrOutcomeCollector>>()));

builder.Services.AddHostedService<CustomsWorkerService>();

await builder.Build().RunAsync();

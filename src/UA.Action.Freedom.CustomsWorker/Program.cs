using System.Diagnostics.Metrics;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using EDI.ELO;
using HMRC.GVMS;
using HMRC.PushPullNotifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.CustomsWorker;
using UA.Action.Freedom.CustomsWorker.Configuration;
using UA.Action.Freedom.CustomsWorker.Customs;
using UA.Action.Freedom.CustomsWorker.Elo;
using UA.Action.Freedom.CustomsWorker.Queueing;
using UA.Action.Freedom.CustomsWorker.Telemetry;
using UA.Action.Freedom.Telemetry;

var builder = Host.CreateApplicationBuilder(args);

builder.AddFreedomTelemetry();

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.Configure<HmrcOptions>(builder.Configuration.GetSection(HmrcOptions.SectionName));
builder.Services.Configure<EloOptions>(builder.Configuration.GetSection(EloOptions.SectionName));

var storage = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
              ?? new StorageOptions();
var hmrc = builder.Configuration.GetSection(HmrcOptions.SectionName).Get<HmrcOptions>()
           ?? new HmrcOptions();
var elo = builder.Configuration.GetSection(EloOptions.SectionName).Get<EloOptions>()
          ?? new EloOptions();

if (!storage.IsConfigured)
{
    throw new InvalidOperationException(
        "Storage:ConnectionString is required. The worker has nothing to do without a queue to read.");
}

if (!elo.IsConfigured)
{
    // Unlike the HMRC SDKs there is no production host to fall back on: the published ELO spec
    // declares no servers: entry, so AddEloClient has no default and would throw with less to go on.
    throw new InvalidOperationException(
        "Elo:BaseUrl is required. The French customs ELO API publishes no default host — use "
        + "https://api.douane.gouv.fr/sibrexit/ in production, https://api-moa.douane.gouv.fr/sibrexit/ "
        + "for certification, or the WireMock stub locally. See docs/schemas/edi/onboarding.md.");
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

// The French customs client, pointed at WireMock locally and at the GUN2 endpoint elsewhere. Unlike
// the HMRC SDKs there is no OAuth handler to chain: the ELO spec declares no security scheme, so the
// bearer token is a method argument and EloTokenProvider is what supplies it.
builder.Services.AddEloClient(options => options.BaseUrl = new Uri(elo.BaseUrl!));
builder.Services.AddHttpClient(EloTokenProvider.ClientName);

// A singleton, because the token cache is the point: §2.2.5.1 asks operators to reuse a token for
// its whole lifetime rather than re-authenticating per call.
builder.Services.AddSingleton<IEloTokenProvider, EloTokenProvider>();

builder.Services.AddFreedomWorkerTelemetry(
    new WatchedQueue(QueueNames.CustomsWork, storage.CustomsQueue, storage.PoisonQueue),
    new WatchedQueue(QueueNames.EloEnvelopes, storage.EloQueue, storage.EloPoisonQueue));
builder.Services.AddSingleton(provider => CustomsMetrics.Create(provider.GetRequiredService<IMeterFactory>()));

builder.Services.AddSingleton<ICustomsWorkQueue, AzureCustomsWorkQueue>();
builder.Services.AddSingleton<IGmrDocumentStore, BlobGmrDocumentStore>();
builder.Services.AddSingleton<IEloWorkQueue, AzureEloWorkQueue>();
builder.Services.AddSingleton<IEloDocumentStore, BlobEloDocumentStore>();

builder.Services.AddSingleton<GmrSubmissionProcessor>();
builder.Services.AddSingleton<EloEnvelopeProcessor>();
builder.Services.AddSingleton(provider => new GmrOutcomeCollector(
    provider.GetRequiredService<IPushPullNotificationsClient>(),
    provider.GetRequiredService<IGmrDocumentStore>(),
    provider.GetRequiredService<IOptions<HmrcOptions>>().Value.Ppns.BoxId
        ?? throw new InvalidOperationException(
            "Hmrc:Ppns:BoxId is required. Without a box there is nowhere to collect GMR outcomes from."),
    provider.GetRequiredService<ILogger<GmrOutcomeCollector>>(),
    provider.GetRequiredService<CustomsMetrics>()));

builder.Services.AddHostedService<CustomsWorkerService>();

await builder.Build().RunAsync();

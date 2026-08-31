using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.ManifestWorker.Configuration;
using UA.Action.Freedom.ManifestWorker.Documents;

namespace UA.Action.Freedom.ManifestWorker;

/// <summary>
/// Drives the manifest document processor.
/// </summary>
/// <remarks>
/// Azure Functions supplies the trigger in the target design — a queue trigger. Here a hosted
/// service does the waking, which keeps the local environment on open tooling and, more
/// usefully, keeps the interesting logic (<see cref="ManifestDocumentProcessor"/>) independent
/// of whatever calls it. Moving to Functions later replaces this file and nothing else.
///
/// Deliberately dumb, and deliberately untested, matching <c>CustomsWorkerService</c>: every
/// decision worth making lives in the processor, where it can be tested without a queue.
/// </remarks>
public sealed class ManifestWorkerService(
    ManifestDocumentProcessor documents,
    IOptions<WorkerOptions> options,
    ILogger<ManifestWorkerService> logger) : BackgroundService
{
    private readonly WorkerOptions _worker = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Manifest Worker started. Queue every {QueueSeconds}s.", _worker.QueuePollSeconds);

        using var idle = new PeriodicTimer(TimeSpan.FromSeconds(_worker.QueuePollSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Keep going while there is work: a convoy's worth of manifests is approved at
                // once, and waiting a poll interval between each would turn a burst into a queue
                // that drains all afternoon.
                while (await documents.ProcessNextAsync(stoppingToken))
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The loop must survive anything the queue or storage does to it. A worker that
                // dies on an unexpected error stops rendering for every other manifest too.
                logger.LogError(exception, "Unhandled error draining the manifest document queue.");
            }

            if (!await SafeWait(idle, stoppingToken))
            {
                return;
            }
        }
    }

    private static async Task<bool> SafeWait(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

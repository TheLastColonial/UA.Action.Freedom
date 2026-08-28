using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.CustomsWorker.Configuration;
using UA.Action.Freedom.CustomsWorker.Customs;

namespace UA.Action.Freedom.CustomsWorker;

/// <summary>
/// Drives both halves of the Customs Worker.
/// </summary>
/// <remarks>
/// Azure Functions supplies the triggers in the target design — a queue trigger for
/// submissions and a timer trigger for outcomes. Here a single hosted service does the
/// waking, which keeps the local environment on open tooling and, more usefully, keeps the
/// interesting logic (<see cref="GmrSubmissionProcessor"/> and
/// <see cref="GmrOutcomeCollector"/>) independent of whatever calls it. Moving to Functions
/// later replaces this file and nothing else.
/// </remarks>
public sealed class CustomsWorkerService(
    GmrSubmissionProcessor submissions,
    GmrOutcomeCollector outcomes,
    IOptions<WorkerOptions> options,
    ILogger<CustomsWorkerService> logger) : BackgroundService
{
    private readonly WorkerOptions _worker = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Customs Worker started. Queue every {QueueSeconds}s, outcomes every {OutcomeSeconds}s.",
            _worker.QueuePollSeconds,
            _worker.OutcomePollSeconds);

        await Task.WhenAll(
            DrainQueue(stoppingToken),
            PollOutcomes(stoppingToken));
    }

    private async Task DrainQueue(CancellationToken stoppingToken)
    {
        using var idle = new PeriodicTimer(TimeSpan.FromSeconds(_worker.QueuePollSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Keep going while there is work: a convoy's worth of manifests arrives at
                // once, and waiting a poll interval between each would turn a burst into a
                // queue that drains all afternoon.
                while (await submissions.ProcessNextAsync(stoppingToken))
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The loop must survive anything the queue or HMRC does to it. A worker that
                // dies on an unexpected error stops submitting for every other manifest too.
                logger.LogError(exception, "Unhandled error draining the customs work queue.");
            }

            if (!await SafeWait(idle, stoppingToken))
            {
                return;
            }
        }
    }

    private async Task PollOutcomes(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_worker.OutcomePollSeconds));

        while (await SafeWait(timer, stoppingToken))
        {
            try
            {
                await outcomes.CollectAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unhandled error collecting goods movement record outcomes.");
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

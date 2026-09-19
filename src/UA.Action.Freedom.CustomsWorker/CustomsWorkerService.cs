using HMRC.GVMS;
using HMRC.PushPullNotifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.CustomsWorker.Configuration;
using UA.Action.Freedom.CustomsWorker.Customs;
using UA.Action.Freedom.CustomsWorker.Telemetry;
using UA.Action.Freedom.Telemetry;

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
/// <para>
/// Each pass of each loop reports a heartbeat, so a loop that has stopped is visible as a stale
/// timestamp rather than looking exactly like a worker with nothing to do.
/// </para>
/// </remarks>
public sealed class CustomsWorkerService(
    GmrSubmissionProcessor submissions,
    GmrOutcomeCollector outcomes,
    IOptions<WorkerOptions> options,
    ILogger<CustomsWorkerService> logger,
    WorkerLoopMetrics? loopMetrics = null) : BackgroundService
{
    private const string DrainLoop = "drain";
    private const string OutcomesLoop = "outcomes";

    private readonly WorkerOptions _worker = options.Value;
    private readonly WorkerLoopMetrics _loops = loopMetrics ?? WorkerLoopMetrics.Unobserved;

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

                _loops.Succeeded(DrainLoop);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The loop must survive anything the queue or HMRC does to it. A worker that
                // dies on an unexpected error stops submitting for every other manifest too.
                _loops.Failed(DrainLoop);
                LogUnhandled(exception, "Unhandled error draining the customs work queue.");
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
            // A span per poll, so HMRC's client span has a parent and a slow or failing poll is
            // findable as one unit.
            using var activity = CustomsMetrics.Source.StartActivity("poll gmr outcomes");

            try
            {
                await outcomes.CollectAsync(stoppingToken);

                _loops.Succeeded(OutcomesLoop);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _loops.Failed(OutcomesLoop);
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error);
                LogUnhandled(exception, "Unhandled error collecting goods movement record outcomes.");
            }
        }
    }

    /// <summary>
    /// Logs an error that escaped a loop. HMRC's API exceptions carry up to 512 characters of the
    /// response body in their message, which can echo a plate or an EORI, so for those only the
    /// type and status are logged.
    /// </summary>
    private void LogUnhandled(Exception exception, string message)
    {
        var status = exception switch
        {
            GvmsApiException api => api.StatusCode,
            PushPullNotificationsApiException ppns => ppns.StatusCode,
            _ => (int?)null,
        };

        if (status is { } code)
        {
            logger.LogError("{Message} HMRC answered {StatusCode} ({ExceptionType}).", message, code, exception.GetType().Name);
            return;
        }

        logger.LogError(exception, "{Message}", message);
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

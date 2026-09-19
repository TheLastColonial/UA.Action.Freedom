using System.Diagnostics;
using System.Reflection;
using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Telemetry;

/// <summary>
/// Wraps a command handler so every invocation is counted, timed and traced — one decorator over
/// every use case instead of a line in each.
/// </summary>
/// <remarks>
/// Applied to every registered <see cref="ICommandHandler{TCommand,TResult}"/> at the end of
/// <c>AddFreedomApplication</c>. Queries are left alone: HTTP request metrics already cover reads.
/// <para>
/// A handler's <em>outcome</em> is its result enum's member name. By convention in this codebase
/// the success case is the first member, so <c>result</c> is <c>ok</c> for member zero and
/// <c>rejected</c> for the rest without any per-enum mapping. An exception's type and message are
/// deliberately never recorded — only that one occurred.
/// </para>
/// </remarks>
public sealed class InstrumentedCommandHandler<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    FreedomMetrics metrics) : ICommandHandler<TCommand, TResult>
{
    private static readonly string HandlerName = NameOf(typeof(TCommand));

    public async Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        using var activity = FreedomMetrics.Source.StartActivity($"command {HandlerName}");
        var started = Stopwatch.GetTimestamp();

        try
        {
            var result = await inner.HandleAsync(command, cancellationToken);
            var (outcome, kind) = Outcome<TResult>.Of(result);

            activity?.SetTag("freedom.outcome", outcome);
            metrics.HandlerCompleted(HandlerName, outcome, kind, Stopwatch.GetElapsedTime(started));

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            metrics.HandlerCompleted(HandlerName, "Cancelled", "cancelled", Stopwatch.GetElapsedTime(started));
            throw;
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            metrics.HandlerCompleted(HandlerName, "Exception", "error", Stopwatch.GetElapsedTime(started));
            throw;
        }
    }

    private static string NameOf(Type command) =>
        command.Name.EndsWith("Command", StringComparison.Ordinal) ? command.Name[..^"Command".Length] : command.Name;

    private static class Outcome<T>
    {
        private static readonly PropertyInfo? Wrapped =
            typeof(T).GetProperty("Outcome") is { PropertyType.IsEnum: true } property ? property : null;

        public static (string Outcome, string Kind) Of(T result)
        {
            if (result is null)
            {
                return ("NoResult", "rejected");
            }

            if (result is Enum member)
            {
                return Classify(member);
            }

            return Wrapped?.GetValue(result) is Enum wrapped ? Classify(wrapped) : ("Succeeded", "ok");
        }

        private static (string Outcome, string Kind) Classify(Enum member) =>
            (member.ToString(), Convert.ToInt64(member, System.Globalization.CultureInfo.InvariantCulture) == 0 ? "ok" : "rejected");
    }
}

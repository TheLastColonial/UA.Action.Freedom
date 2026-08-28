namespace UA.Action.Freedom.Application.Abstractions;

/// <summary>
/// Handles a command — a request to change state. The result type describes the outcome
/// (created, conflicted, not found) so the caller can map it without catching exceptions
/// for control flow.
/// </summary>
public interface ICommandHandler<in TCommand, TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

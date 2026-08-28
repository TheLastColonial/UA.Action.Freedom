namespace UA.Action.Freedom.Application.Abstractions;

/// <summary>
/// Handles a query — a read that returns data and changes nothing.
/// </summary>
public interface IQueryHandler<in TQuery, TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

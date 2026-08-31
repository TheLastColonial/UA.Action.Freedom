using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>The route of a convoy, or <c>null</c> if there is no such convoy.</summary>
/// <remarks>
/// A convoy with no stops and a convoy that does not exist are different answers — an empty
/// list and a 404 — so this query cannot simply return the rows it found.
/// </remarks>
public sealed record GetConvoyRouteQuery(int ConvoyId);

public sealed class GetConvoyRouteHandler(IConvoyRepository repository)
    : IQueryHandler<GetConvoyRouteQuery, IReadOnlyList<RouteStopReadModel>?>
{
    public async Task<IReadOnlyList<RouteStopReadModel>?> HandleAsync(
        GetConvoyRouteQuery query, CancellationToken cancellationToken)
    {
        if (!await repository.ExistsAsync(query.ConvoyId, cancellationToken))
        {
            return null;
        }

        return await repository.GetRouteAsync(query.ConvoyId, cancellationToken);
    }
}

/// <summary>
/// Replace a convoy's whole route. The order of <see cref="Stops"/> is the journey.
/// </summary>
public sealed record ReplaceConvoyRouteCommand(int ConvoyId, IReadOnlyList<RouteStopReadModel> Stops);

public enum ReplaceConvoyRouteOutcome
{
    Replaced,
    NotFound
}

public sealed class ReplaceConvoyRouteHandler(IConvoyRepository repository)
    : ICommandHandler<ReplaceConvoyRouteCommand, ReplaceConvoyRouteOutcome>
{
    public async Task<ReplaceConvoyRouteOutcome> HandleAsync(
        ReplaceConvoyRouteCommand command, CancellationToken cancellationToken)
    {
        if (!await repository.ExistsAsync(command.ConvoyId, cancellationToken))
        {
            return ReplaceConvoyRouteOutcome.NotFound;
        }

        // Renumber into a dense 1..n sequence in list order. Whatever the caller put in the
        // Sequence fields, the order they sent the stops in is the journey they meant; trusting
        // their numbering would let duplicates or gaps store a different route than was entered.
        var ordered = command.Stops
            .Select((stop, index) => stop with { Sequence = index + 1 })
            .ToList();

        await repository.ReplaceRouteAsync(command.ConvoyId, ordered, cancellationToken);

        return ReplaceConvoyRouteOutcome.Replaced;
    }
}

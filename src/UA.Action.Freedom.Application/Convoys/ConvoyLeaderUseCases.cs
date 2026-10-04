using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>The leader of a convoy and its history, or null if there is no such convoy.</summary>
public sealed record GetConvoyLeaderQuery(int ConvoyId);

public sealed class GetConvoyLeaderHandler(IConvoyRepository convoys, IConvoyLeaderRepository leaders)
    : IQueryHandler<GetConvoyLeaderQuery, ConvoyLeaderReadModel?>
{
    public async Task<ConvoyLeaderReadModel?> HandleAsync(GetConvoyLeaderQuery query, CancellationToken cancellationToken)
    {
        if (!await convoys.ExistsAsync(query.ConvoyId, cancellationToken))
        {
            return null;
        }

        var history = await leaders.HistoryAsync(query.ConvoyId, cancellationToken);
        return new ConvoyLeaderReadModel(history.FirstOrDefault(assignment => assignment.Until is null), history);
    }
}

/// <summary>Make a volunteer the leader of a convoy, ending the previous leader's assignment.</summary>
public sealed record NominateConvoyLeaderCommand(int ConvoyId, Guid PersonId);

public enum NominateConvoyLeaderOutcome
{
    Nominated,
    ConvoyNotFound,
    ConvoyArrived,
    NotADriverOnConvoy,
    AlreadyLeader
}

public sealed class NominateConvoyLeaderHandler(IConvoyRepository convoys, IConvoyLeaderRepository leaders)
    : ICommandHandler<NominateConvoyLeaderCommand, NominateConvoyLeaderOutcome>
{
    public async Task<NominateConvoyLeaderOutcome> HandleAsync(
        NominateConvoyLeaderCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(command.ConvoyId, cancellationToken);
        if (convoy is null)
        {
            return NominateConvoyLeaderOutcome.ConvoyNotFound;
        }

        if (convoy.Arrived)
        {
            return NominateConvoyLeaderOutcome.ConvoyArrived;
        }

        return await leaders.NominateAsync(command.ConvoyId, command.PersonId, DateTime.UtcNow, cancellationToken) switch
        {
            NominateLeaderResult.Nominated => NominateConvoyLeaderOutcome.Nominated,
            NominateLeaderResult.AlreadyLeader => NominateConvoyLeaderOutcome.AlreadyLeader,
            _ => NominateConvoyLeaderOutcome.NotADriverOnConvoy,
        };
    }
}

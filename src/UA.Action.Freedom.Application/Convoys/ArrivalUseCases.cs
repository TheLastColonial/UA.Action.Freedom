using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>Mark a convoy arrived: the journey is over.</summary>
public sealed record ArriveConvoyCommand(int ConvoyId);

public enum ArriveConvoyOutcome
{
    Arrived,
    NotFound,
    TruckListNotPublished,
    AlreadyArrived,
    VehiclesStillTravelling
}

/// <param name="StillTravelling">When refused for that reason, the VINs with no finished manifest.</param>
public sealed record ArriveConvoyResult(ArriveConvoyOutcome Outcome, IReadOnlyList<string> StillTravelling)
{
    public static ArriveConvoyResult Of(ArriveConvoyOutcome outcome) => new(outcome, []);
}

/// <summary>
/// A convoy has arrived when every vehicle on it has a finished manifest — Delivered, Lost or
/// Returned. Delivered and Lost vehicles are handed over (they are part of the aid and stay in
/// Ukraine) and are never offered for a convoy again; Returned ones are released to travel again.
/// </summary>
public sealed class ArriveConvoyHandler(IConvoyRepository repository)
    : ICommandHandler<ArriveConvoyCommand, ArriveConvoyResult>
{
    public async Task<ArriveConvoyResult> HandleAsync(ArriveConvoyCommand command, CancellationToken cancellationToken)
    {
        var convoy = await repository.GetByIdAsync(command.ConvoyId, cancellationToken);

        if (convoy is null)
        {
            return ArriveConvoyResult.Of(ArriveConvoyOutcome.NotFound);
        }

        if (convoy.Arrived)
        {
            return ArriveConvoyResult.Of(ArriveConvoyOutcome.AlreadyArrived);
        }

        if (!convoy.TruckListPublished)
        {
            return ArriveConvoyResult.Of(ArriveConvoyOutcome.TruckListNotPublished);
        }

        // The repository re-checks all three inside its transaction; this read only makes the
        // common refusals cheap and specific.
        return await repository.ArriveAsync(command.ConvoyId, DateTime.UtcNow, cancellationToken) switch
        {
            ArriveResult.Arrived => ArriveConvoyResult.Of(ArriveConvoyOutcome.Arrived),
            ArriveResult.AlreadyArrived => ArriveConvoyResult.Of(ArriveConvoyOutcome.AlreadyArrived),
            _ => new ArriveConvoyResult(
                ArriveConvoyOutcome.VehiclesStillTravelling,
                await repository.ListVehiclesStillTravellingAsync(command.ConvoyId, cancellationToken)),
        };
    }
}

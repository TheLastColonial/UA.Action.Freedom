using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>
/// Publish the convoy's truck list — the set of vehicles committed to it.
/// </summary>
/// <remarks>
/// docs/process.puml orders the work <em>Truck List Created → Truck List Published → Manifest
/// Proposed</em>. Publication is the gate: it is what manifests are proposed against, so it
/// happens once and it closes the vehicle list to additions.
/// </remarks>
public sealed record PublishTruckListCommand(int ConvoyId);

public enum PublishTruckListOutcome
{
    Published,
    NotFound,
    AlreadyPublished
}

public sealed class PublishTruckListHandler(IConvoyRepository repository)
    : ICommandHandler<PublishTruckListCommand, PublishTruckListOutcome>
{
    public async Task<PublishTruckListOutcome> HandleAsync(
        PublishTruckListCommand command, CancellationToken cancellationToken)
    {
        // The write is conditional on nothing having published yet, so two dispatchers pressing
        // publish at the same moment cannot both succeed. Only the loser pays for a second read.
        if (await repository.PublishTruckListAsync(command.ConvoyId, DateTime.UtcNow, cancellationToken))
        {
            return PublishTruckListOutcome.Published;
        }

        var convoy = await repository.GetByIdAsync(command.ConvoyId, cancellationToken);

        return convoy is null
            ? PublishTruckListOutcome.NotFound
            : PublishTruckListOutcome.AlreadyPublished;
    }
}

/// <summary>Put a vehicle on a convoy's truck list.</summary>
public sealed record AssignVehicleToConvoyCommand(int ConvoyId, string Vin);

public enum AssignVehicleOutcome
{
    Assigned,
    ConvoyNotFound,
    VehicleNotFound,
    TruckListPublished,
    VehicleNotPassedInspection,
    VehicleOnAnotherConvoy,
    VehicleHandedOver,
    ConvoyArrived
}

public sealed class AssignVehicleToConvoyHandler(IConvoyRepository convoys, IConvoyVehicleRepository truckList)
    : ICommandHandler<AssignVehicleToConvoyCommand, AssignVehicleOutcome>
{
    public async Task<AssignVehicleOutcome> HandleAsync(
        AssignVehicleToConvoyCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(command.ConvoyId, cancellationToken);

        if (convoy is null)
        {
            return AssignVehicleOutcome.ConvoyNotFound;
        }

        if (convoy.Arrived)
        {
            return AssignVehicleOutcome.ConvoyArrived;
        }

        // Manifests are proposed against the published list. Adding a vehicle afterwards would
        // put a truck on the road that no manifest describes.
        if (convoy.TruckListPublished)
        {
            return AssignVehicleOutcome.TruckListPublished;
        }

        // A donated vehicle is itself part of the aid, handed over in Ukraine: one that has not
        // passed its servicing inspection is a failed delivery waiting to happen.
        return await truckList.AddAsync(command.ConvoyId, command.Vin, cancellationToken) switch
        {
            AddToTruckListResult.Added => AssignVehicleOutcome.Assigned,
            AddToTruckListResult.AlreadyOnThisConvoy => AssignVehicleOutcome.Assigned,
            AddToTruckListResult.NotPassedInspection => AssignVehicleOutcome.VehicleNotPassedInspection,
            AddToTruckListResult.OnAnotherConvoy => AssignVehicleOutcome.VehicleOnAnotherConvoy,
            AddToTruckListResult.HandedOver => AssignVehicleOutcome.VehicleHandedOver,
            _ => AssignVehicleOutcome.VehicleNotFound,
        };
    }
}

/// <summary>
/// Take a vehicle off a convoy, or — once the truck list is published — record that it left.
/// </summary>
/// <remarks>
/// Before publication nothing downstream depends on the list, so the entry is simply deleted with
/// its crew and insurance. Afterwards it is a <em>withdrawal</em>: a vehicle that breaks down near
/// Poznan leaves the convoy, but its manifest and its Goods Movement Reference still describe a
/// real load, and the record of which convoy it set off with is part of what happened. The one
/// endpoint covers both because the caller is doing the same thing — taking a truck off a convoy —
/// and which of the two it means is a fact about the convoy, not about the request.
/// </remarks>
public sealed record UnassignVehicleFromConvoyCommand(int ConvoyId, string Vin, string? Reason = null);

public enum UnassignVehicleOutcome
{
    Unassigned,
    Withdrawn,
    ConvoyNotFound,
    NotOnThisConvoy,
    AlreadyWithdrawn,
    ConvoyArrived
}

public sealed class UnassignVehicleFromConvoyHandler(IConvoyRepository convoys, IConvoyVehicleRepository truckList)
    : ICommandHandler<UnassignVehicleFromConvoyCommand, UnassignVehicleOutcome>
{
    public async Task<UnassignVehicleOutcome> HandleAsync(
        UnassignVehicleFromConvoyCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(command.ConvoyId, cancellationToken);

        if (convoy is null)
        {
            return UnassignVehicleOutcome.ConvoyNotFound;
        }

        // The journey is over and its truck list is the record of who went.
        if (convoy.Arrived)
        {
            return UnassignVehicleOutcome.ConvoyArrived;
        }

        if (!convoy.TruckListPublished)
        {
            return await truckList.RemoveAsync(command.ConvoyId, command.Vin, cancellationToken)
                ? UnassignVehicleOutcome.Unassigned
                : UnassignVehicleOutcome.NotOnThisConvoy;
        }

        var entry = await truckList.GetAsync(command.ConvoyId, command.Vin, cancellationToken);

        if (entry is null)
        {
            return UnassignVehicleOutcome.NotOnThisConvoy;
        }

        if (entry.Withdrawn)
        {
            return UnassignVehicleOutcome.AlreadyWithdrawn;
        }

        return await truckList.WithdrawAsync(
            command.ConvoyId, command.Vin, command.Reason, DateTime.UtcNow, cancellationToken)
            ? UnassignVehicleOutcome.Withdrawn
            : UnassignVehicleOutcome.AlreadyWithdrawn;
    }
}

/// <summary>The vehicles on a convoy's truck list, or <c>null</c> if there is no such convoy.</summary>
public sealed record ListConvoyVehiclesQuery(int ConvoyId);

public sealed class ListConvoyVehiclesHandler(IConvoyRepository convoys, IConvoyVehicleRepository truckList)
    : IQueryHandler<ListConvoyVehiclesQuery, IReadOnlyList<ConvoyVehicleReadModel>?>
{
    public async Task<IReadOnlyList<ConvoyVehicleReadModel>?> HandleAsync(
        ListConvoyVehiclesQuery query, CancellationToken cancellationToken)
    {
        if (!await convoys.ExistsAsync(query.ConvoyId, cancellationToken))
        {
            return null;
        }

        // Withdrawn vehicles are included: the list is the record of what set off, not only of
        // what is still moving, and each entry says which it is.
        return await truckList.ListAsync(query.ConvoyId, cancellationToken);
    }
}

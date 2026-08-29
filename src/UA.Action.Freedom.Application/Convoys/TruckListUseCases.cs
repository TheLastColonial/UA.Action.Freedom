using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>
/// Publish the convoy's truck list — the set of vehicles committed to it.
/// </summary>
/// <remarks>
/// docs/process.puml orders the work <em>Truck List Created → Truck List Published → Manifest
/// Proposed</em>. Publication is the gate: it is what manifests are proposed against, so it
/// happens once and it closes the vehicle list.
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
    TruckListPublished
}

public sealed class AssignVehicleToConvoyHandler(IConvoyRepository repository)
    : ICommandHandler<AssignVehicleToConvoyCommand, AssignVehicleOutcome>
{
    public async Task<AssignVehicleOutcome> HandleAsync(
        AssignVehicleToConvoyCommand command, CancellationToken cancellationToken)
    {
        var convoy = await repository.GetByIdAsync(command.ConvoyId, cancellationToken);

        if (convoy is null)
        {
            return AssignVehicleOutcome.ConvoyNotFound;
        }

        // Manifests are proposed against the published list. Adding a vehicle afterwards would
        // put a truck on the road that no manifest describes.
        if (convoy.TruckListPublished)
        {
            return AssignVehicleOutcome.TruckListPublished;
        }

        return await repository.AssignVehicleAsync(command.ConvoyId, command.Vin, cancellationToken)
            ? AssignVehicleOutcome.Assigned
            : AssignVehicleOutcome.VehicleNotFound;
    }
}

/// <summary>Take a vehicle off a convoy's truck list.</summary>
public sealed record UnassignVehicleFromConvoyCommand(int ConvoyId, string Vin);

public enum UnassignVehicleOutcome
{
    Unassigned,
    ConvoyNotFound,
    NotOnThisConvoy,
    TruckListPublished
}

public sealed class UnassignVehicleFromConvoyHandler(IConvoyRepository repository)
    : ICommandHandler<UnassignVehicleFromConvoyCommand, UnassignVehicleOutcome>
{
    public async Task<UnassignVehicleOutcome> HandleAsync(
        UnassignVehicleFromConvoyCommand command, CancellationToken cancellationToken)
    {
        var convoy = await repository.GetByIdAsync(command.ConvoyId, cancellationToken);

        if (convoy is null)
        {
            return UnassignVehicleOutcome.ConvoyNotFound;
        }

        // Removing a vehicle after publication would leave a manifest describing a truck that is
        // no longer travelling — and nobody would find out until loading day.
        if (convoy.TruckListPublished)
        {
            return UnassignVehicleOutcome.TruckListPublished;
        }

        return await repository.UnassignVehicleAsync(command.ConvoyId, command.Vin, cancellationToken)
            ? UnassignVehicleOutcome.Unassigned
            : UnassignVehicleOutcome.NotOnThisConvoy;
    }
}

/// <summary>The vehicles on a convoy's truck list, or <c>null</c> if there is no such convoy.</summary>
public sealed record ListConvoyVehiclesQuery(int ConvoyId);

public sealed class ListConvoyVehiclesHandler(IConvoyRepository repository)
    : IQueryHandler<ListConvoyVehiclesQuery, IReadOnlyList<ConvoyVehicleReadModel>?>
{
    public async Task<IReadOnlyList<ConvoyVehicleReadModel>?> HandleAsync(
        ListConvoyVehiclesQuery query, CancellationToken cancellationToken)
    {
        if (!await repository.ExistsAsync(query.ConvoyId, cancellationToken))
        {
            return null;
        }

        return await repository.ListVehiclesAsync(query.ConvoyId, cancellationToken);
    }
}

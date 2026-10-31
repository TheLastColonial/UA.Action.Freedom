using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Locations;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Boxes;

/// <summary>Record how a box ended on a convoy. The Dispatcher acts for the Convoy Leader until scoped permissions.</summary>
public sealed record RecordBoxOutcomeCommand(
    int ConvoyId, int BoxId, BoxOutcome Outcome, string? Reason, int? HubLocationId);

public enum RecordBoxOutcomeOutcome
{
    Recorded,
    ConvoyNotFound,
    ConvoyNotDeparted,
    BoxNotOnConvoy,
    IllegalTransition,
    ReasonRequired,
    HubNotRegistered,
    Conflict,
}

/// <summary>Every box's current outcome on a convoy, or null when there is no such convoy.</summary>
public sealed record ListBoxOutcomesQuery(int ConvoyId);

public sealed class ListBoxOutcomesHandler(IConvoyRepository convoys, IBoxOutcomeRepository outcomes)
    : IQueryHandler<ListBoxOutcomesQuery, IReadOnlyList<BoxOutcomeReadModel>?>
{
    public async Task<IReadOnlyList<BoxOutcomeReadModel>?> HandleAsync(
        ListBoxOutcomesQuery query, CancellationToken cancellationToken) =>
        await convoys.ExistsAsync(query.ConvoyId, cancellationToken)
            ? await outcomes.ListCurrentAsync(query.ConvoyId, cancellationToken)
            : null;
}

/// <summary>Mark a vehicle delivered: the Convoy Leader act, performed by the Dispatcher until plan 17 (O8).</summary>
public sealed record DeliverVehicleCommand(int ConvoyId, string Vin);

public enum DeliverVehicleOutcome
{
    Delivered,
    ConvoyNotFound,
    ConvoyNotDeparted,
    ConvoyArrived,
    VehicleNotOnConvoy,
    VehicleWithdrawn,
    AlreadyDelivered,
}

public sealed record DeliverVehicleResult(DeliverVehicleOutcome Outcome, int BoxesDelivered = 0);

/// <remarks>
/// Transitional: the manifest still holds <c>Delivered</c>, and <c>ArriveAsync</c>, <c>HandedOverAt</c> and the erasure
/// guard still read it, so the vehicle manifest is mirrored to <c>Delivered</c> here. Plan 15 switches those readers
/// to outcomes and removes the mirror.
/// </remarks>
public sealed class DeliverVehicleHandler(
    IConvoyRepository convoys,
    IConvoyVehicleRepository truckList,
    IBoxOutcomeRepository outcomes,
    IManifestRepository manifests)
    : ICommandHandler<DeliverVehicleCommand, DeliverVehicleResult>
{
    public async Task<DeliverVehicleResult> HandleAsync(
        DeliverVehicleCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(command.ConvoyId, cancellationToken);

        if (convoy is null)
        {
            return new DeliverVehicleResult(DeliverVehicleOutcome.ConvoyNotFound);
        }

        if (!convoy.Departed)
        {
            return new DeliverVehicleResult(DeliverVehicleOutcome.ConvoyNotDeparted);
        }

        if (convoy.Arrived)
        {
            return new DeliverVehicleResult(DeliverVehicleOutcome.ConvoyArrived);
        }

        var entry = await truckList.GetAsync(command.ConvoyId, command.Vin, cancellationToken);

        if (entry is null)
        {
            return new DeliverVehicleResult(DeliverVehicleOutcome.VehicleNotOnConvoy);
        }

        if (entry.Withdrawn)
        {
            return new DeliverVehicleResult(DeliverVehicleOutcome.VehicleWithdrawn);
        }

        if (entry.Delivered)
        {
            return new DeliverVehicleResult(DeliverVehicleOutcome.AlreadyDelivered);
        }

        if (await outcomes.DeliverVehicleAsync(command.ConvoyId, command.Vin, cancellationToken) is not { } boxesDelivered)
        {
            return new DeliverVehicleResult(DeliverVehicleOutcome.AlreadyDelivered);
        }

        await MirrorToManifestAsync(command, cancellationToken);

        return new DeliverVehicleResult(DeliverVehicleOutcome.Delivered, boxesDelivered);
    }

    private async Task MirrorToManifestAsync(DeliverVehicleCommand command, CancellationToken cancellationToken)
    {
        var manifest = await manifests.GetForVehicleAsync(command.ConvoyId, command.Vin, cancellationToken);

        if (manifest is not null && ManifestTransitions.CanTransition(manifest.Status, ManifestStatus.Delivered))
        {
            await manifests.TransitionAsync(manifest.Id, manifest.Status, ManifestStatus.Delivered, cancellationToken);
        }
    }
}

/// <summary>Accept a Ukrainian goods list: every delivered box for that Receiver on that vehicle becomes accepted.</summary>
public sealed record AcceptGoodsListCommand(int ConvoyId, string Vin, Guid ReceiverRef);

public enum AcceptGoodsListOutcome
{
    Accepted,
    ConvoyNotFound,
    ConvoyNotDeparted,
    VehicleNotOnConvoy,
    NothingToAccept,
}

public sealed record AcceptGoodsListResult(AcceptGoodsListOutcome Outcome, int BoxesAccepted = 0);

public sealed class AcceptGoodsListHandler(
    IConvoyRepository convoys,
    IConvoyVehicleRepository truckList,
    IBoxOutcomeRepository outcomes)
    : ICommandHandler<AcceptGoodsListCommand, AcceptGoodsListResult>
{
    public async Task<AcceptGoodsListResult> HandleAsync(
        AcceptGoodsListCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(command.ConvoyId, cancellationToken);

        if (convoy is null)
        {
            return new AcceptGoodsListResult(AcceptGoodsListOutcome.ConvoyNotFound);
        }

        if (!convoy.Departed)
        {
            return new AcceptGoodsListResult(AcceptGoodsListOutcome.ConvoyNotDeparted);
        }

        if (await truckList.GetAsync(command.ConvoyId, command.Vin, cancellationToken) is null)
        {
            return new AcceptGoodsListResult(AcceptGoodsListOutcome.VehicleNotOnConvoy);
        }

        var accepted = await outcomes.AcceptGoodsListAsync(
            command.ConvoyId, command.Vin, command.ReceiverRef, cancellationToken);

        return accepted == 0
            ? new AcceptGoodsListResult(AcceptGoodsListOutcome.NothingToAccept)
            : new AcceptGoodsListResult(AcceptGoodsListOutcome.Accepted, accepted);
    }
}

public sealed class RecordBoxOutcomeHandler(
    IConvoyRepository convoys,
    IConvoyVehicleRepository truckList,
    ILocationRepository locations,
    IBoxOutcomeRepository outcomes)
    : ICommandHandler<RecordBoxOutcomeCommand, RecordBoxOutcomeOutcome>
{
    public async Task<RecordBoxOutcomeOutcome> HandleAsync(
        RecordBoxOutcomeCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(command.ConvoyId, cancellationToken);

        if (convoy is null)
        {
            return RecordBoxOutcomeOutcome.ConvoyNotFound;
        }

        if (!convoy.Departed)
        {
            return RecordBoxOutcomeOutcome.ConvoyNotDeparted;
        }

        var current = await outcomes.GetCurrentAsync(command.ConvoyId, command.BoxId, cancellationToken);
        var allocation = await truckList.GetBoxAllocationAsync(command.BoxId, cancellationToken);

        // A box returned to a hub no longer has an allocation: that is a finished box, not a missing one.
        if (allocation is null || allocation.ConvoyId.Value != command.ConvoyId)
        {
            return current is null
                ? RecordBoxOutcomeOutcome.BoxNotOnConvoy
                : RecordBoxOutcomeOutcome.IllegalTransition;
        }

        if (!BoxOutcomes.CanRecord(current?.Outcome, command.Outcome))
        {
            return RecordBoxOutcomeOutcome.IllegalTransition;
        }

        var hub = command.HubLocationId is { } hubId
            ? await locations.GetByIdAsync(hubId, cancellationToken)
            : null;

        return BoxOutcomes.Check(command.Outcome, command.Reason, hub?.IsRegisteredHub ?? false) switch
        {
            BoxOutcomeCheck.ReasonRequired => RecordBoxOutcomeOutcome.ReasonRequired,
            BoxOutcomeCheck.HubNotRegistered => RecordBoxOutcomeOutcome.HubNotRegistered,
            _ => await Write(command, allocation.Vin, current?.Outcome, cancellationToken),
        };
    }

    private async Task<RecordBoxOutcomeOutcome> Write(
        RecordBoxOutcomeCommand command, string vin, BoxOutcome? from, CancellationToken cancellationToken)
    {
        var result = await outcomes.RecordAsync(
            new RecordBoxOutcome(
                command.ConvoyId, vin, command.BoxId, from, command.Outcome, command.Reason, command.HubLocationId),
            cancellationToken);

        return result == RecordBoxOutcomeResult.Recorded
            ? RecordBoxOutcomeOutcome.Recorded
            : RecordBoxOutcomeOutcome.Conflict;
    }
}

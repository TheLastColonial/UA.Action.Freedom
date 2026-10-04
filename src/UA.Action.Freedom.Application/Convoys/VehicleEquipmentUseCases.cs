using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Convoys;

public sealed record ListEquipmentItemsQuery;

public sealed class ListEquipmentItemsHandler(IVehicleEquipmentRepository equipment)
    : IQueryHandler<ListEquipmentItemsQuery, IReadOnlyList<EquipmentItemReadModel>>
{
    public Task<IReadOnlyList<EquipmentItemReadModel>> HandleAsync(
        ListEquipmentItemsQuery query, CancellationToken cancellationToken) =>
        equipment.ListItemsAsync(cancellationToken);
}

public sealed record AddEquipmentItemCommand(string Name, decimal? UnitCostGbp);

public enum AddEquipmentItemOutcome
{
    Created,
    NameTaken
}

public sealed record AddEquipmentItemResult(AddEquipmentItemOutcome Outcome, int? Id = null);

public sealed class AddEquipmentItemHandler(IVehicleEquipmentRepository equipment)
    : ICommandHandler<AddEquipmentItemCommand, AddEquipmentItemResult>
{
    public async Task<AddEquipmentItemResult> HandleAsync(
        AddEquipmentItemCommand command, CancellationToken cancellationToken)
    {
        var id = await equipment.AddItemAsync(command.Name, command.UnitCostGbp, cancellationToken);

        return id is { } created
            ? new AddEquipmentItemResult(AddEquipmentItemOutcome.Created, created)
            : new AddEquipmentItemResult(AddEquipmentItemOutcome.NameTaken);
    }
}

public sealed record GetVehicleEquipmentQuery(int ConvoyId, string Vin);

/// <summary>The equipment on a vehicle, or null when that vehicle is not on this convoy.</summary>
public sealed class GetVehicleEquipmentHandler(IConvoyVehicleRepository truckList, IVehicleEquipmentRepository equipment)
    : IQueryHandler<GetVehicleEquipmentQuery, IReadOnlyList<VehicleEquipmentReadModel>?>
{
    public async Task<IReadOnlyList<VehicleEquipmentReadModel>?> HandleAsync(
        GetVehicleEquipmentQuery query, CancellationToken cancellationToken) =>
        await truckList.GetAsync(query.ConvoyId, query.Vin, cancellationToken) is null
            ? null
            : await equipment.ListForVehicleAsync(query.ConvoyId, query.Vin, cancellationToken);
}

public sealed record SetVehicleEquipmentCommand(int ConvoyId, string Vin, IReadOnlyList<VehicleEquipmentLine> Lines);

public enum SetVehicleEquipmentOutcome
{
    Set,
    ConvoyNotFound,
    VehicleNotOnConvoy,
    UnknownItem,
    ConvoyArrived
}

/// <summary>
/// Replace the equipment the charity has bought for a vehicle. A step of creating a convoy (O13), skippable and
/// never required to depart.
/// </summary>
public sealed class SetVehicleEquipmentHandler(IConvoyRepository convoys, IVehicleEquipmentRepository equipment)
    : ICommandHandler<SetVehicleEquipmentCommand, SetVehicleEquipmentOutcome>
{
    public async Task<SetVehicleEquipmentOutcome> HandleAsync(
        SetVehicleEquipmentCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(command.ConvoyId, cancellationToken);

        if (convoy is null)
        {
            return SetVehicleEquipmentOutcome.ConvoyNotFound;
        }

        if (convoy.Arrived)
        {
            return SetVehicleEquipmentOutcome.ConvoyArrived;
        }

        return await equipment.ReplaceForVehicleAsync(command.ConvoyId, command.Vin, command.Lines, cancellationToken) switch
        {
            ReplaceEquipmentResult.Replaced => SetVehicleEquipmentOutcome.Set,
            ReplaceEquipmentResult.UnknownItem => SetVehicleEquipmentOutcome.UnknownItem,
            _ => SetVehicleEquipmentOutcome.VehicleNotOnConvoy,
        };
    }
}

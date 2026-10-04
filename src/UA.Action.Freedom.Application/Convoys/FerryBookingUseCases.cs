using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>
/// What is written when a ferry booking is recorded: one vehicle's outbound crossing (P1). There is
/// no return leg to book, because vehicles are handed over in Ukraine rather than driven back.
/// </summary>
public sealed record FerryBookingRecord(
    int ConvoyId,
    string Vin,
    string Operator,
    string Reference,
    DateTime SailingAt,
    string? TicketDetails,
    decimal? CostGbp);

public sealed record FerryBookingReadModel(
    int ConvoyId,
    string Vin,
    string Operator,
    string Reference,
    DateTime SailingAt,
    string? TicketDetails,
    decimal? CostGbp,
    string? LastChangedByName = null,
    DateTime? LastChangedAt = null);

public sealed record RecordFerryBookingCommand(FerryBookingRecord Booking);

public enum RecordFerryBookingOutcome
{
    Recorded,
    ConvoyNotFound,
    VehicleNotOnConvoy,
    ConvoyArrived
}

/// <summary>
/// Record (or replace) a vehicle's ferry booking. Whether the vehicle is still travelling with the
/// convoy is settled by the write, as it is for the insurance.
/// </summary>
public sealed class RecordFerryBookingHandler(IConvoyRepository convoys, IConvoyVehicleRepository truckList)
    : ICommandHandler<RecordFerryBookingCommand, RecordFerryBookingOutcome>
{
    public async Task<RecordFerryBookingOutcome> HandleAsync(
        RecordFerryBookingCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(command.Booking.ConvoyId, cancellationToken);

        if (convoy is null)
        {
            return RecordFerryBookingOutcome.ConvoyNotFound;
        }

        if (convoy.Arrived)
        {
            return RecordFerryBookingOutcome.ConvoyArrived;
        }

        return await truckList.RecordFerryBookingAsync(command.Booking, cancellationToken)
            ? RecordFerryBookingOutcome.Recorded
            : RecordFerryBookingOutcome.VehicleNotOnConvoy;
    }
}

public sealed record GetFerryBookingQuery(int ConvoyId, string Vin);

public sealed class GetFerryBookingHandler(IConvoyVehicleRepository truckList)
    : IQueryHandler<GetFerryBookingQuery, FerryBookingReadModel?>
{
    public Task<FerryBookingReadModel?> HandleAsync(GetFerryBookingQuery query, CancellationToken cancellationToken) =>
        truckList.GetFerryBookingAsync(query.ConvoyId, query.Vin, cancellationToken);
}

public sealed record RemoveFerryBookingCommand(int ConvoyId, string Vin);

public enum RemoveFerryBookingOutcome
{
    Removed,
    NotFound,
    ConvoyArrived
}

public sealed class RemoveFerryBookingHandler(IConvoyRepository convoys, IConvoyVehicleRepository truckList)
    : ICommandHandler<RemoveFerryBookingCommand, RemoveFerryBookingOutcome>
{
    public async Task<RemoveFerryBookingOutcome> HandleAsync(
        RemoveFerryBookingCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(command.ConvoyId, cancellationToken);

        if (convoy?.Arrived ?? false)
        {
            return RemoveFerryBookingOutcome.ConvoyArrived;
        }

        return await truckList.RemoveFerryBookingAsync(command.ConvoyId, command.Vin, cancellationToken)
            ? RemoveFerryBookingOutcome.Removed
            : RemoveFerryBookingOutcome.NotFound;
    }
}

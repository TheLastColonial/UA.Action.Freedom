using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>
/// What is written when insurance is recorded. <see cref="RecordedBy"/> is the caller's token
/// subject, supplied by the endpoint rather than the request body.
/// </summary>
public sealed record VehicleInsuranceRecord(
    int ConvoyId,
    string Vin,
    string Insurer,
    string PolicyNumber,
    DateTime CoverStart,
    DateTime CoverEnd,
    decimal? CostGbp,
    string RecordedBy);

/// <summary>A vehicle's insurance for one convoy, as stored — including whether a crew change voided it.</summary>
public sealed record VehicleInsuranceReadModel(
    int ConvoyId,
    string Vin,
    string Insurer,
    string PolicyNumber,
    DateTime CoverStart,
    DateTime CoverEnd,
    decimal? CostGbp,
    string RecordedBy,
    DateTime RecordedAt,
    DateTime? VoidedAt)
{
    public bool Voided => VoidedAt is not null;

    public bool CoversOn(DateTime day) => VehicleInsurance.InCover(CoverStart, CoverEnd, VoidedAt, day);
}

public sealed record RecordInsuranceCommand(VehicleInsuranceRecord Insurance);

public enum RecordInsuranceOutcome
{
    Recorded,
    ConvoyNotFound,
    VehicleNotOnConvoy,
    ConvoyArrived
}

/// <summary>
/// Record (or replace) a vehicle's insurance for a convoy. Recording again after a crew change is
/// how a voided policy is renewed, so a replacement clears <c>VoidedAt</c>.
/// </summary>
public sealed class RecordInsuranceHandler(IConvoyRepository convoys, IConvoyVehicleRepository truckList)
    : ICommandHandler<RecordInsuranceCommand, RecordInsuranceOutcome>
{
    public async Task<RecordInsuranceOutcome> HandleAsync(
        RecordInsuranceCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(command.Insurance.ConvoyId, cancellationToken);

        if (convoy is null)
        {
            return RecordInsuranceOutcome.ConvoyNotFound;
        }

        if (convoy.Arrived)
        {
            return RecordInsuranceOutcome.ConvoyArrived;
        }

        return await truckList.RecordInsuranceAsync(command.Insurance, cancellationToken)
            ? RecordInsuranceOutcome.Recorded
            : RecordInsuranceOutcome.VehicleNotOnConvoy;
    }
}

public sealed record GetInsuranceQuery(int ConvoyId, string Vin);

public sealed class GetInsuranceHandler(IConvoyVehicleRepository truckList)
    : IQueryHandler<GetInsuranceQuery, VehicleInsuranceReadModel?>
{
    public Task<VehicleInsuranceReadModel?> HandleAsync(GetInsuranceQuery query, CancellationToken cancellationToken) =>
        truckList.GetInsuranceAsync(query.ConvoyId, query.Vin, cancellationToken);
}

public sealed record RemoveInsuranceCommand(int ConvoyId, string Vin);

public enum RemoveInsuranceOutcome
{
    Removed,
    NotFound,
    ConvoyArrived
}

public sealed class RemoveInsuranceHandler(IConvoyRepository convoys, IConvoyVehicleRepository truckList)
    : ICommandHandler<RemoveInsuranceCommand, RemoveInsuranceOutcome>
{
    public async Task<RemoveInsuranceOutcome> HandleAsync(
        RemoveInsuranceCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(command.ConvoyId, cancellationToken);

        if (convoy?.Arrived ?? false)
        {
            return RemoveInsuranceOutcome.ConvoyArrived;
        }

        return await truckList.RemoveInsuranceAsync(command.ConvoyId, command.Vin, cancellationToken)
            ? RemoveInsuranceOutcome.Removed
            : RemoveInsuranceOutcome.NotFound;
    }
}

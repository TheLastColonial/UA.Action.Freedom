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
    VehicleNotOnConvoy
}

/// <summary>
/// Record (or replace) a vehicle's insurance for a convoy. Recording again after a crew change is
/// how a voided policy is renewed, so a replacement clears <c>VoidedAt</c>.
/// </summary>
public sealed class RecordInsuranceHandler(IConvoyRepository repository)
    : ICommandHandler<RecordInsuranceCommand, RecordInsuranceOutcome>
{
    public async Task<RecordInsuranceOutcome> HandleAsync(
        RecordInsuranceCommand command, CancellationToken cancellationToken)
    {
        if (!await repository.ExistsAsync(command.Insurance.ConvoyId, cancellationToken))
        {
            return RecordInsuranceOutcome.ConvoyNotFound;
        }

        return await repository.RecordInsuranceAsync(command.Insurance, cancellationToken)
            ? RecordInsuranceOutcome.Recorded
            : RecordInsuranceOutcome.VehicleNotOnConvoy;
    }
}

public sealed record GetInsuranceQuery(int ConvoyId, string Vin);

public sealed class GetInsuranceHandler(IConvoyRepository repository)
    : IQueryHandler<GetInsuranceQuery, VehicleInsuranceReadModel?>
{
    public Task<VehicleInsuranceReadModel?> HandleAsync(GetInsuranceQuery query, CancellationToken cancellationToken) =>
        repository.GetInsuranceAsync(query.ConvoyId, query.Vin, cancellationToken);
}

public sealed record RemoveInsuranceCommand(int ConvoyId, string Vin);

public enum RemoveInsuranceOutcome
{
    Removed,
    NotFound
}

public sealed class RemoveInsuranceHandler(IConvoyRepository repository)
    : ICommandHandler<RemoveInsuranceCommand, RemoveInsuranceOutcome>
{
    public async Task<RemoveInsuranceOutcome> HandleAsync(
        RemoveInsuranceCommand command, CancellationToken cancellationToken) =>
        await repository.RemoveInsuranceAsync(command.ConvoyId, command.Vin, cancellationToken)
            ? RemoveInsuranceOutcome.Removed
            : RemoveInsuranceOutcome.NotFound;
}

using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Vehicles;

/// <summary>
/// A Mechanic's inspection result for the vehicle identified by <see cref="Vin"/>. Separate from
/// <see cref="UpdateVehicleCommand"/> because it is a different act by a different role, and
/// because only a <see cref="InspectionStatus.Passed"/> vehicle may join a convoy.
/// </summary>
public sealed record RecordInspectionCommand(string Vin, InspectionStatus Status, string? Notes);

public enum RecordInspectionOutcome
{
    Recorded,
    NotFound
}

public sealed class RecordInspectionHandler(IVehicleRepository repository)
    : ICommandHandler<RecordInspectionCommand, RecordInspectionOutcome>
{
    public async Task<RecordInspectionOutcome> HandleAsync(
        RecordInspectionCommand command, CancellationToken cancellationToken)
    {
        var notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes;

        var recorded = await repository.RecordInspectionAsync(command.Vin, command.Status, notes, cancellationToken);

        return recorded ? RecordInspectionOutcome.Recorded : RecordInspectionOutcome.NotFound;
    }
}

using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Boxes;

/// <summary>
/// How one box ended on one convoy. The reason is a bounded code and the hub is a location, never
/// a Receiver or an address, so a report built from this has nothing sensitive to leak.
/// </summary>
public sealed record BoxOutcomeReadModel(
    int BoxId,
    int ConvoyId,
    string Vin,
    BoxOutcome Outcome,
    string? Reason,
    int? HubLocationId,
    DateTime RecordedAt,
    string? RecordedByName = null);

/// <summary>
/// A change of outcome. <paramref name="From"/> is the outcome the caller saw (null: none yet), so the write
/// can settle a race rather than a read-then-write in C#. The caller is the attribution, never a field.
/// </summary>
public sealed record RecordBoxOutcome(
    int ConvoyId,
    string Vin,
    int BoxId,
    BoxOutcome? From,
    BoxOutcome Outcome,
    string? Reason,
    int? HubLocationId);

public enum RecordBoxOutcomeResult
{
    Recorded,
    Conflict,
}

/// <summary>
/// Persistence port for box outcomes (<c>dbo.BoxOutcome</c>): one current row per box per convoy, history kept.
/// </summary>
public interface IBoxOutcomeRepository
{
    Task<BoxOutcomeReadModel?> GetCurrentAsync(int convoyId, int boxId, CancellationToken cancellationToken);

    /// <summary>The current outcome of every box that has one on the convoy.</summary>
    Task<IReadOnlyList<BoxOutcomeReadModel>> ListCurrentAsync(int convoyId, CancellationToken cancellationToken);

    /// <summary>
    /// Makes <see cref="RecordBoxOutcome.Outcome"/> the box's current outcome in one transaction. A return to a hub
    /// also takes the box off its vehicle and puts it at the hub, so it can be allocated again.
    /// </summary>
    Task<RecordBoxOutcomeResult> RecordAsync(RecordBoxOutcome outcome, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the vehicle delivered and gives every box allocated to it that has no outcome yet the outcome
    /// <see cref="BoxOutcome.Delivered"/>, in one transaction. Returns how many boxes were delivered, or null when the
    /// vehicle was already delivered (or has left the convoy), in which case nothing is written.
    /// </summary>
    Task<int?> DeliverVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken);

    /// <summary>
    /// Accepts, in one transaction, every box of this Receiver that is currently <see cref="BoxOutcome.Delivered"/> on
    /// this vehicle (O9, O24). Returns how many were accepted; boxes with any other outcome, or for another Receiver,
    /// are untouched.
    /// </summary>
    Task<int> AcceptGoodsListAsync(int convoyId, string vin, Guid receiverRef, CancellationToken cancellationToken);
}

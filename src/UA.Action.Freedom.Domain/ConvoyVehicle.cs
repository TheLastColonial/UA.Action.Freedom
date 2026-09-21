namespace UA.Action.Freedom.Domain;

/// <summary>
/// One <see cref="Vehicle"/> on one <see cref="Convoy"/> — an entry on the truck list.
/// </summary>
/// <remarks>
/// This is the single statement of "this vehicle is travelling with this convoy". It used to be
/// four: a mutable pointer on the vehicle, two loose foreign keys on the manifest, and the keys of
/// the crew and insurance tables, with nothing reconciling them. The crew, the insurance and the
/// <see cref="Manifest"/> now all hang off this entry, so a manifest cannot describe a truck that
/// is not on the list.
///
/// <para>
/// Withdrawal is a stamp, not a delete. A vehicle that breaks down leaves the convoy — it may be
/// repaired and join a later one, or make its own way — but its manifest and its Goods Movement
/// Reference still describe a real load, and the record of which convoy it set off with is part of
/// what happened. Deleting the row would take the crew and the insurance with it and leave the
/// manifest pointing at nothing.
/// </para>
/// </remarks>
public sealed class ConvoyVehicle
{
    public required ConvoyId ConvoyId { get; init; }

    /// <summary>The <see cref="Vehicle.VIN"/> of the vehicle on the list.</summary>
    public required string Vin { get; init; }

    /// <summary>When the Dispatcher put the vehicle on the truck list.</summary>
    public DateTime AddedAt { get; init; }

    /// <summary>
    /// When the vehicle left the convoy, if it did. Null for a vehicle that is still with it.
    /// </summary>
    public DateTime? WithdrawnAt { get; init; }

    /// <summary>Why it left — a breakdown, an accident, a border refusal. Null while it is with the convoy.</summary>
    public string? WithdrawnReason { get; init; }

    /// <summary>Whether the vehicle is still travelling with the convoy.</summary>
    public bool Travelling => IsTravelling(this.WithdrawnAt);

    /// <summary>Whether the vehicle has left the convoy.</summary>
    public bool Withdrawn => !this.Travelling;

    /// <summary>
    /// The one statement of whether a truck-list entry still counts — shared with the read side,
    /// the way <see cref="VehicleInsurance.InCover"/> is.
    /// </summary>
    /// <remarks>
    /// Readiness, arrival and the "is this vehicle free for another convoy" check all ask this
    /// question, and each of them getting it subtly different is how the four copies started.
    /// </remarks>
    public static bool IsTravelling(DateTime? withdrawnAt) => withdrawnAt is null;
}

namespace UA.Action.Freedom.Domain;

/// <summary>
/// Collection of <see cref="Vehicle"/>s transiting together
/// </summary>
public class Convoy
{
    /// <summary>
    /// Unique reference
    /// </summary>
    public required ConvoyId Id { get; init; }

    /// <summary>
    /// The truck list: one entry per vehicle travelling with this convoy, including those that
    /// have since been withdrawn. See <see cref="ConvoyVehicle"/>.
    /// </summary>
    public List<ConvoyVehicle> TruckList { get; init; } = [];

    /// <summary>
    /// Departure Timestamp
    /// </summary>
    public DateTime Start { get; init; }

    /// <summary>
    /// Arrival Timestamp (Expected due to issues in transit)
    /// </summary>
    public DateTime ExpectedEnd { get; init; }

    /// <summary>
    /// When the truck list was published. Null while the convoy is still being planned.
    /// </summary>
    /// <remarks>
    /// docs/process.puml puts <em>Truck List Published</em> before <em>Manifest Proposed</em>:
    /// manifests are proposed against the set of vehicles committed to the convoy, so there has to
    /// be a published set first.
    /// </remarks>
    public DateTime? TruckListPublishedAt { get; init; }

    /// <summary>
    /// When the convoy arrived and the journey became history. Null while it is still travelling.
    /// </summary>
    /// <remarks>
    /// After this nothing about the convoy changes: its crew and insurance are the record of who
    /// went and under what cover, and a vehicle that was not handed over is free to travel again
    /// without any pointer needing to be cleared.
    /// </remarks>
    public DateTime? ArrivedAt { get; init; }

    /// <summary>
    /// Steps that will be taken by the <see cref="Convoy"/>
    /// </summary>
    public Route Route { get; init; } = [];

    /// <summary>
    /// Whether the set of vehicles is closed to additions. Manifests are proposed against a
    /// published truck list, so publication fixes what is on it (docs/process.puml).
    /// </summary>
    public bool TruckListPublished => this.TruckListPublishedAt is not null;

    /// <summary>Whether the journey is over.</summary>
    public bool Arrived => this.ArrivedAt is not null;
}

/// <summary>
/// Unique Id of a <see cref="Convoy"/>
/// </summary>
/// <param name="Value"></param>
public record ConvoyId(int Value);

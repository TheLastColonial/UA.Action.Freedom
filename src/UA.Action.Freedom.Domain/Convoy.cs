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

    public List<Vehicle> Vehicles { get; init; } = [];

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
    /// Steps that will be taken by the <see cref="Convoy"/>
    /// </summary>
    public Route Route { get; init; } = [];
}

/// <summary>
/// Unique Id of a <see cref="Convoy"/>
/// </summary>
/// <param name="Value"></param>
public record ConvoyId(int Value);

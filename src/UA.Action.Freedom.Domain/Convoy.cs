namespace UA.Action.Freedom.Domain;

/// <summary>
/// Collection of <see cref="Veichle"/> tranisting together
/// </summary>
public class Convoy
{
    /// <summary>
    /// Unique refernence
    /// </summary>
    public ConvoyId Id { get; init; }

    public List<Veichle> Veichles { get; init; }

    /// <summary>
    /// Departure Timestamp
    /// </summary>
    public DateTime Start { get; init; }

    /// <summary>
    /// Arrival Timestamp (Expected due to issues in transit)
    /// </summary>
    public DateTime ExpectedEnd { get; init; }

    /// <summary>
    /// Steps that will be taken by the <see cref="Convoy"/>
    /// </summary>
    public Route Route { get; init; }
}

/// <summary>
/// Unique Id of a <see cref="Convoy"/>
/// </summary>
/// <param name="Value"></param>
public record ConvoyId(int Value);
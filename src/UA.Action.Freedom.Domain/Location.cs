namespace UA.Action.Freedom.Domain;

/// <summary>
/// A distribution hub — a garage or warehouse — where <see cref="Box"/>es are stored between
/// arriving and being loaded for a convoy. Subdivided into <see cref="Bay"/>s.
/// </summary>
public class Location
{
    /// <summary>Unique reference</summary>
    public required LocationId Id { get; init; }

    /// <summary>Human-readable name, e.g. "Coventry Depot"</summary>
    public required string Name { get; init; }

    /// <summary>Where the location is in the real world</summary>
    public Address? Address { get; init; }
}

/// <summary>
/// Unique reference to a <see cref="Location"/>
/// </summary>
/// <param name="Value"></param>
public record LocationId(int Value);

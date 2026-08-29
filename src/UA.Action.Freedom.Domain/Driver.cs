namespace UA.Action.Freedom.Domain;

/// <summary>
/// A <see cref="Person"/> who drives a leg of a <see cref="Convoy"/>
/// </summary>
public class Driver : Person
{
    /// <summary>
    /// Convoys this driver has been part of
    /// </summary>
    public Convoy[] Convoys { get; set; } = [];

    /// <summary>
    /// Committed to the next convoy, as opposed to merely available
    /// </summary>
    public bool Committed { get; set; }
}

/// <summary>
/// The primary and secondary <see cref="Driver"/> pair for one leg of a <see cref="Convoy"/>
/// </summary>
public class DriverTeam
{
    /// <summary>
    /// Unique reference. A team is only ever reached through the manifest that assigned it.
    /// </summary>
    public required DriverTeamId Id { get; set; }

    public required Driver PrimaryDriver { get; set; }

    /// <summary>
    /// The second driver, once one has been found. A team may be half-crewed while it is planned.
    /// </summary>
    public Driver? SecondaryDriver { get; set; }

    public Driver[] All => this.SecondaryDriver is null
        ? [this.PrimaryDriver]
        : [this.PrimaryDriver, this.SecondaryDriver];
}

/// <summary>
/// Unique reference to a <see cref="DriverTeam"/>
/// </summary>
/// <param name="Value"></param>
public record DriverTeamId(int Value);

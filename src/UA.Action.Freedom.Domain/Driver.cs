namespace UA.Action.Freedom.Domain;

public class Driver : Person
{
    public Convoy[] Convoys { get; set; }
    public bool Committed { get; set; }
}

public enum ConvoyRole
{
    Unknown = 0,
    Driver,
    Passenger,
    TeamLeader
}

public class DriverTeam
{
    public Driver PrimaryDriver { get; set; }
    public Driver SecondaryDriver { get; set; }
    public Driver[] All => new[] { PrimaryDriver, SecondaryDriver };
}

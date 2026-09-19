namespace UA.Action.Freedom.Domain;

/// <summary>
/// What a crew member does on a vehicle for one convoy. A driver must be a volunteer registered
/// to drive; a passenger can be any volunteer. A vehicle is ready to travel with two drivers —
/// passengers do not count towards that.
/// </summary>
public enum CrewRole
{
    Driver = 0,
    Passenger = 1
}

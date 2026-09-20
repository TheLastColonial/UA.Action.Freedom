namespace UA.Action.Freedom.Domain;

/// <summary>
/// A <see cref="Person"/> who drives a leg of a <see cref="Convoy"/>
/// </summary>
/// <remarks>
/// Which convoys a driver has been on is not a field here: it is the set of crew rows naming them,
/// which is also what the insurance and the erasure check read. A second copy on the person would
/// be one more thing to keep in step.
/// </remarks>
public class Driver : Person
{
    /// <summary>
    /// Committed to the next convoy, as opposed to merely available
    /// </summary>
    public bool Committed { get; set; }
}

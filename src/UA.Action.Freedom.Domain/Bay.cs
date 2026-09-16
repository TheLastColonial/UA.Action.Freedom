namespace UA.Action.Freedom.Domain;

/// <summary>
/// A 1m by 1m storage area within a <see cref="Domain.Location"/>, where a checked, weighed
/// and measured <see cref="Box"/> is placed so a Loader can find it again.
/// </summary>
/// <remarks>
/// A bay may hold several boxes at once — there is no per-bay uniqueness. What is enforced is
/// the other direction: a box may only be in one bay, within one location, at a time (see
/// <see cref="BoxBayAssignment"/>). A bay's <see cref="Code"/> is unique only within its own
/// location, not globally — two depots may each have a bay called "A1".
/// </remarks>
public class Bay
{
    /// <summary>Unique reference</summary>
    public required BayId Id { get; init; }

    /// <summary>The <see cref="Domain.Location"/> this bay belongs to</summary>
    public required LocationId LocationId { get; init; }

    /// <summary>Short code identifying the bay within its location, e.g. "A3"</summary>
    public required string Code { get; init; }
}

/// <summary>
/// Unique reference to a <see cref="Bay"/>
/// </summary>
/// <param name="Value"></param>
public record BayId(int Value);

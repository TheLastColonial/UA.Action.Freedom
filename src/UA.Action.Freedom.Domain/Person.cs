namespace UA.Action.Freedom.Domain;

/// <summary>
/// Individual supporting Ukrainian Action
/// </summary>
/// <remarks>
/// Volunteer personal data: UK residency, never written to logs, defined retention period
/// (docs/recommendations.md §4.8). The identifier is a <see cref="Guid"/> rather than a sequence
/// so that it can appear in a URL without disclosing how many volunteers there are.
/// </remarks>
public class Person
{
    /// <summary>
    /// Unique reference
    /// </summary>
    public required PersonId Id { get; set; }

    public required string FirstName { get; set; }

    public required string LastName { get; set; }

    public DateTime DateOfBirth { get; set; }

    /// <summary>
    /// When they joined Ukrainian Action
    /// </summary>
    public DateTime Joined { get; set; }

    public string? Phone { get; set; }
}

/// <summary>
/// Unique reference to a <see cref="Person"/>
/// </summary>
/// <param name="Value"></param>
public record PersonId(Guid Value);

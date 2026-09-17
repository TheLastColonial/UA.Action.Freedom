namespace UA.Action.Freedom.Domain;

/// <summary>
/// A record of a <see cref="Box"/> having been placed in a <see cref="Domain.Bay"/> — who put
/// it there, and when. Mirrors <see cref="BoxQrCode"/>'s issue/revoke shape.
/// </summary>
/// <remarks>
/// A box can be moved to a new bay. Assigning one revokes (vacates) any bay the box currently
/// occupies, so at most one row per box has <see cref="VacatedAt"/> null. Vacated rows are
/// kept, not deleted — the history of where a box has been is worth keeping even after it has
/// moved on.
/// </remarks>
public record BoxBayAssignment
{
    /// <summary>Unique reference</summary>
    public required BoxBayAssignmentId Id { get; init; }

    /// <summary>The box this assignment is for</summary>
    public required BoxId BoxId { get; init; }

    /// <summary>The bay the box was placed in</summary>
    public required BayId BayId { get; init; }

    /// <summary>Who placed the box in this bay</summary>
    public required PersonId AssignedByPersonId { get; init; }

    /// <summary>When the box was placed in this bay</summary>
    public required DateTime AssignedAt { get; init; }

    /// <summary>When the box was moved out of this bay, or null while it is still there</summary>
    public DateTime? VacatedAt { get; init; }

    /// <summary>Whether the box is currently in this bay</summary>
    public bool Active => this.VacatedAt is null;
}

/// <summary>
/// Unique reference to a <see cref="BoxBayAssignment"/>
/// </summary>
/// <param name="Value"></param>
public record BoxBayAssignmentId(int Value);

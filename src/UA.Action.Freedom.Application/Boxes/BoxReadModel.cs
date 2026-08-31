namespace UA.Action.Freedom.Application.Boxes;

/// <summary>
/// A packed box as this slice persists and returns it.
/// </summary>
/// <remarks>
/// <see cref="ValidatedByPersonId"/> and <see cref="ValidatedAt"/> together are an audit
/// artefact, not a status flag. Validation is the trust boundary between the donor and
/// Ukrainian Action — a Loader physically checks what is in the box — and the weight it
/// confirms is what a border check relies on (docs/domain/key-concepts.md § Box).
///
/// <see cref="ReceiverRef"/> is the opaque reference only. The delivery address lives behind
/// the Ground Officer role and never comes near a box.
/// </remarks>
public sealed record BoxReadModel(
    int Id,
    int WeightKg,
    Guid? ReceiverRef,
    string? House,
    string? Street,
    string? City,
    string? Country,
    string? Postcode,
    Guid? ValidatedByPersonId,
    DateTime? ValidatedAt)
{
    /// <summary>Whether a Loader has confirmed the contents and the weight.</summary>
    public bool Validated => this.ValidatedAt is not null;
}

/// <summary>
/// A single donated thing inside a box. Tracked as contents, never individually in transit.
/// </summary>
public sealed record BoxItemReadModel(
    Guid Id,
    string Description,
    IReadOnlyDictionary<string, string> Properties);

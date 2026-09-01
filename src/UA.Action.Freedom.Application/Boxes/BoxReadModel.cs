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

/// <summary>
/// A QR label issued for a box: an opaque, non-enumerable token a scanner resolves back to the
/// box's record.
/// </summary>
/// <remarks>
/// A box can be re-labelled — issuing a new code revokes the previous one — so at most one code
/// per box is active. <see cref="RevokedAt"/> is the whole history: revoked rows are kept, not
/// deleted, so a label found in the wild can always be told from an unknown one.
///
/// The token is the only identifier that ever appears on the physical label. The label may be
/// inspected at a border, so it carries no receiver, address or contents
/// (docs/domain/key-concepts.md § Data Sensitivity).
/// </remarks>
public sealed record BoxQrCodeReadModel(
    Guid Token,
    int BoxId,
    DateTime IssuedAt,
    DateTime? RevokedAt)
{
    /// <summary>Whether this is the code a scan currently resolves to.</summary>
    public bool Active => this.RevokedAt is null;
}

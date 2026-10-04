using UA.Action.Freedom.Domain;

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
///
/// <see cref="VoidedAt"/>, <see cref="ReplacesBoxId"/> and <see cref="ReplacedByBoxId"/> are the replacement lineage
/// (ADR 0011): an attested box is never edited, it is voided and a new box takes its place.
///
/// <see cref="LocationId"/> is the distribution hub the box currently sits in, if it has
/// arrived at one — independent of which bay it has been shelved in (see
/// <see cref="BoxBayAssignmentReadModel"/>), which tracks its own history.
/// </remarks>
public sealed record BoxReadModel(
    int Id,
    int WeightKg,
    decimal? WidthCm,
    decimal? DepthCm,
    decimal? HeightCm,
    Guid? ReceiverRef,
    int? LocationId,
    Guid? ValidatedByPersonId,
    DateTime? ValidatedAt,
    string? LastChangedByName = null,
    DateTime? LastChangedAt = null,
    DateTime? VoidedAt = null,
    int? ReplacesBoxId = null,
    int? ReplacedByBoxId = null)
{
    /// <summary>Whether a Loader has confirmed the contents and the weight.</summary>
    public bool Validated => this.ValidatedAt is not null;

    /// <summary>
    /// Whether the box has been replaced (ADR 0011). A voided box is terminal: it stays as the record of what
    /// was attested, but it carries no cargo and counts in no total.
    /// </summary>
    public bool Voided => this.VoidedAt is not null;
}

/// <summary>
/// A single donated thing inside a box. Tracked as contents, never individually in transit.
/// </summary>
/// <param name="CategoryId">What kind of thing it is (ADR 0014). It carries the hazard flags and the customs codes.</param>
/// <param name="CommodityCode">
/// The commodity code this item is declared under on an ICS2 Entry Summary Declaration, at least six
/// digits — <c>EnsCommodity.HumanitarianAid</c> covers aid. A field of its own rather than a key in
/// <paramref name="Properties"/>: the properties are open-ended precisely because nothing depends on
/// them, and a border refusal turns on this. <see langword="null"/> when the item has no code of its own,
/// in which case its category's code for the authority being filed with applies, and the filing sheet
/// reports it as missing only when neither exists.
/// </param>
/// <param name="ValueGbp">What it is worth in pounds, with no conversion, and <paramref name="ValueSource"/> says who gave the figure (D5).</param>
/// <param name="ExpiresOn">When it expires. An item already past it blocks validation of its box (D1).</param>
/// <param name="DonationId">The drop-off this item came in, if one was recorded (ADR 0013); null for items that predate donations.</param>
/// <param name="CategoryNameEn">Read-time: the category's name, so a client need not look it up.</param>
/// <param name="IsNotCarried">Read-time: the category is one the convoy will not take (D21).</param>
/// <param name="ShelfLife">Read-time: expired, short-dated by the category's rule, or fine (D1, D25).</param>
public sealed record BoxItemReadModel(
    Guid Id,
    string Description,
    IReadOnlyDictionary<string, string> Properties,
    int CategoryId,
    string? CommodityCode = null,
    int? Quantity = null,
    decimal? ValueGbp = null,
    ValueSource? ValueSource = null,
    DateOnly? ExpiresOn = null,
    string? CategoryNameEn = null,
    bool IsNotCarried = false,
    ShelfLifeStatus ShelfLife = ShelfLifeStatus.Fine,
    int? DonationId = null);

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

/// <summary>
/// A record of a box having been placed in a bay: who put it there, and when. At most one row
/// per box is active; earlier rows are kept as history rather than deleted.
/// </summary>
public sealed record BoxBayAssignmentReadModel(
    int Id,
    int BoxId,
    int BayId,
    Guid AssignedByPersonId,
    DateTime AssignedAt,
    DateTime? VacatedAt)
{
    /// <summary>Whether the box is currently in this bay.</summary>
    public bool Active => this.VacatedAt is null;
}

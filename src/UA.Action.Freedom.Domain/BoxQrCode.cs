namespace UA.Action.Freedom.Domain;

/// <summary>
/// An opaque label tying a physical <see cref="Box"/> to its record. Scanning it resolves to
/// the box; the token is the only thing printed on the label.
/// </summary>
/// <remarks>
/// A box can be re-labelled. Issuing a new code revokes the previous one, so at most one code
/// per box is <see cref="Active"/>. Revoked codes are kept, not forgotten — a label recovered
/// later can still be identified.
/// </remarks>
public record BoxQrCode
{
    /// <summary>The opaque, non-enumerable value the label carries and a scan resolves.</summary>
    public required Guid Token { get; init; }

    /// <summary>The box this label belongs to.</summary>
    public required BoxId Box { get; init; }

    /// <summary>When the code was issued.</summary>
    public required DateTime IssuedAt { get; init; }

    /// <summary>When the code was revoked, or null while it is the box's live label.</summary>
    public DateTime? RevokedAt { get; init; }

    /// <summary>Whether a scan of the box currently resolves to this code.</summary>
    public bool Active => this.RevokedAt is null;
}

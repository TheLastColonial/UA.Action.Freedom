namespace UA.Action.Freedom.Domain;

/// <summary>
/// Packaged <see cref="Item"/>s in a container for transport
/// </summary>
public class Box
{
    /// <summary>
    /// Unique reference
    /// </summary>
    public required BoxId Id { get; init; }

    /// <summary>
    /// <see cref="Item"/>s packaged into the <see cref="Box"/>
    /// </summary>
    public List<Item> Items { get; init; } = [];

    /// <summary>
    /// Confirmed weight of the <see cref="Box"/>
    /// </summary>
    public int WeightKg { get; init; }

    /// <summary>Width of the box, in centimetres. Set alongside <see cref="WeightKg"/> at validation. Null until then.</summary>
    public decimal? WidthCm { get; init; }

    /// <summary>Depth of the box, in centimetres. Set alongside <see cref="WeightKg"/> at validation. Null until then.</summary>
    public decimal? DepthCm { get; init; }

    /// <summary>Height of the box, in centimetres. Set alongside <see cref="WeightKg"/> at validation. Null until then.</summary>
    public decimal? HeightCm { get; init; }

    /// <summary>
    /// The contents of the box have been validated
    /// </summary>
    public bool Validated => this.ValidatedBy is not null;

    /// <summary>
    /// Who validated the contents of the box
    /// </summary>
    /// <remarks>
    /// With <see cref="ValidatedAt"/> this is an audit artefact, not a status flag: validation is
    /// the trust boundary between the donor and Ukrainian Action, and the weight it confirms is
    /// what the border check relies on. See docs/domain/key-concepts.md § Box.
    /// </remarks>
    public Person? ValidatedBy { get; init; }

    /// <summary>
    /// When the box was validated. Null while it is unvalidated.
    /// </summary>
    public DateTime? ValidatedAt { get; init; }

    /// <summary>
    /// The distribution hub the <see cref="Box"/> currently sits in, if it has arrived at one.
    /// Independent of which <see cref="Bay"/> it has been placed in, if any — a box can be
    /// checked in at a location before a Loader shelves it (see <see cref="BoxBayAssignment"/>).
    /// </summary>
    public LocationId? LocationId { get; init; }

    /// <summary>
    /// Ultimate <see cref="Domain.Receiver"/> of the box contents
    /// </summary>
    public Receiver? Receiver { get; init; }
}

/// <summary>
/// Unique reference to a <see cref="Box"/>
/// </summary>
/// <param name="Value"></param>
public record BoxId(int Value);

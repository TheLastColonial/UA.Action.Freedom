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
    /// Current location of the <see cref="Box"/>
    /// </summary>
    public Address? Location { get; init; }

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

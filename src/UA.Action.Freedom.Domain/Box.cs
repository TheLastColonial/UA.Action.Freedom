namespace UA.Action.Freedom.Domain;

/// <summary>
/// Packaged <see cref="Item[]"/> into a container for transport
/// </summary>
public class Box
{
    /// <summary>
    /// Unique reference
    /// </summary>
    public BoxId Id { get; init; }

    /// <summary>
    /// <see cref="Item"/> packaged into the <see cref="Box"/>
    /// </summary>
    public List<Item> Items { get; init; }

    /// <summary>
    /// Confirmed weight of the <see cref="Box"/>
    /// </summary>
    public int WeightKg { get; init; } = 0;

    /// <summary>
    /// The contents of the box has been validated
    /// </summary>
    public bool Validated => this.ValidatedBy != null;

    /// <summary>
    /// Who validated the contents of the box
    /// </summary>
    public Person? ValidatedBy { get; init; }

    /// <summary>
    /// When the box was validated
    /// </summary>
    public DateTime ValidatedAt { get; init; }

    /// <summary>
    /// Current location of the <see cref="Box"/>
    /// </summary>
    public Address Location { get; init; }

    /// <summary>
    /// Ultimate <see cref="Reciever"/> of the box contents
    /// </summary>
    public Reciever Reciever { get; init; }
}

/// <summary>
/// Unique reference to a <see cref="Box"/>
/// </summary>
/// <param name="value"></param>
public record BoxId(int value);


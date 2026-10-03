namespace UA.Action.Freedom.Domain;

/// <summary>
/// A single donated thing. Tracked as the contents of a <see cref="Box"/>, never individually in transit.
/// </summary>
public class Item
{
    public Guid Id { get; set; }

    public required string Description { get; set; }

    public int CategoryId { get; set; }

    /// <summary>
    /// The code this item is declared under. Its own when somebody set one, otherwise the one its category maps to.
    /// </summary>
    public string? CommodityCode { get; set; }

    public ItemValue? Value { get; set; }

    public int? Quantity { get; set; }

    public DateOnly? ExpiresOn { get; set; }

    /// <summary>
    /// Open-ended attributes — size, condition and whatever else a donation turns out to need.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = [];
}

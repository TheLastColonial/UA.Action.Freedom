namespace UA.Action.Freedom.Domain;

/// <summary>
/// A single donated thing. Tracked as the contents of a <see cref="Box"/>, never individually in transit.
/// </summary>
public class Item
{
    public Guid Id { get; set; }

    public required string Description { get; set; }

    /// <summary>
    /// Open-ended attributes — size, condition, expiry and whatever else a donation turns out to need.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = [];
}

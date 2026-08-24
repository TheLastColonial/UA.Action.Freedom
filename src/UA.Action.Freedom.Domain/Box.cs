namespace UA.Action.Freedom.Domain;

/// <summary>
/// Packaged <see cref="Item"/> into a container
/// </summary>
public class Box
{
    public int Id { get; set; }
    public Item[] Items { get; set; }
    public int WeightKg { get; set; } = 0;
    public bool Validated { get; set; } = false;
    public Address Location { get; set; }
    public Destination Destination { get; set; }
}

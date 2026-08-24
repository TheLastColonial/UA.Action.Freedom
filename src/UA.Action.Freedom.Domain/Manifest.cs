namespace UA.Action.Freedom.Domain;

/// <summary>
/// List of Boxes allocated to a 
/// </summary>
public class Manifest
{
    public ManifestId Id { get; set; }
    public Veichle Veichle { get; set; }
    public Box[] Boxes { get; set; }

    public int TotalWeightKg()
    {
        return this.Veichle.WeightKg + this.Boxes.Sum(box => box.WeightKg);
    }
}

public record ManifestId(string Value);

public record ManifestStatus
{
    public int Id { get; set; }
    public string Name { get; set; }

    protected ManifestStatus(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public ManifestStatus Proposed => new ManifestStatus(1, "Proposed");
    public ManifestStatus Confirmed => new ManifestStatus(2, "Confirmed");
    public ManifestStatus Prepared => new ManifestStatus(3, "Prepared");
    public ManifestStatus Transit => new ManifestStatus(4, "Transit");
    public ManifestStatus Arrived => new ManifestStatus(5, "Arrived");
    public ManifestStatus Lost => new ManifestStatus(6, "Lost");
}

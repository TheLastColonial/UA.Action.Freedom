namespace UA.Action.Freedom.Domain;

/// <summary>
/// Location in the real world
/// </summary>
public class Address
{
    public string? House { get; init; }
    public string? Street { get; init; }
    public string? City { get; init; }
    public string? Country { get; init; }
    public string Postcode { get; init; } = string.Empty;
}
namespace UA.Action.Freedom.Application.Locations;

/// <summary>
/// A distribution hub (garage or warehouse) as this slice persists and returns it.
/// </summary>
public sealed record LocationReadModel(
    int Id,
    string Name,
    string? House,
    string? Street,
    string? City,
    string? Country,
    string? Postcode);

/// <summary>
/// A 1m by 1m storage bay within a location, as this slice persists and returns it.
/// </summary>
public sealed record BayReadModel(
    int Id,
    int LocationId,
    string Code);

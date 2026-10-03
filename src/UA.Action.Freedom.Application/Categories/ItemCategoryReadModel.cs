using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Categories;

/// <summary>
/// A category of donated item as this slice persists and returns it, with the customs code it maps to for each
/// authority (ADR 0014). A flat shape on purpose: Dapper maps rows, not object graphs.
/// </summary>
/// <param name="WarnWithinDays">
/// How close to expiry an item of this category counts as short-dated, or <see langword="null"/> for no warning. The
/// thresholds are unverified (D25), so they are data the Administrator changes rather than constants.
/// </param>
public sealed record ItemCategoryReadModel(
    int Id,
    string NameEn,
    string NameUk,
    bool IsFixed,
    int? HazardClass,
    bool IsSensitive,
    bool IsNotCarried,
    int? WarnWithinDays,
    string? UkCode = null,
    string? EuCode = null,
    string? UaCode = null,
    string? LastChangedByName = null,
    DateTime? LastChangedAt = null)
{
    public ShelfLifeRule ShelfLife => new(WarnWithinDays);

    public string? CodeFor(CustomsAuthority authority) => authority switch
    {
        CustomsAuthority.UK => UkCode,
        CustomsAuthority.EU => EuCode,
        _ => UaCode,
    };
}

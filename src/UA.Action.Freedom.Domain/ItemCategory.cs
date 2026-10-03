namespace UA.Action.Freedom.Domain;

/// <summary>Who gave the figure on an <see cref="ItemValue"/>. Stored as an integer, so values are never reused.</summary>
public enum ValueSource
{
    /// <summary>The donor stated it.</summary>
    Donor = 0,

    /// <summary>A volunteer estimated it, in pounds. No currency is converted (D5).</summary>
    Estimate = 1,

    /// <summary>The price paid. Only a vehicle is bought; an item is given.</summary>
    Purchased = 2,
}

/// <summary>A value in pounds sterling and where the figure came from (D5, O11, O33).</summary>
public sealed record ItemValue(decimal Gbp, ValueSource Source);

/// <summary>The authority a customs code is declared to. Stored as an integer.</summary>
public enum CustomsAuthority
{
    UK = 0,
    EU = 1,
    UA = 2,
}

/// <summary>
/// How soon before it expires an item counts as short-dated. The threshold is unverified (D25), so it is
/// configuration the Administrator holds on a category, not a constant here. <see langword="null"/> means
/// the category has no short-dated warning.
/// </summary>
public sealed record ShelfLifeRule(int? WarnWithinDays)
{
    public static ShelfLifeRule None { get; } = new(WarnWithinDays: null);
}

public enum ShelfLifeStatus
{
    Fine,
    Short,
    Expired,
}

public static class ShelfLife
{
    /// <summary>
    /// Expired once the date has passed, short once it is within the rule's window, otherwise fine. An item with
    /// no expiry date is fine.
    /// </summary>
    public static ShelfLifeStatus Assess(DateOnly? expiresOn, ShelfLifeRule rule, DateOnly asOf)
    {
        if (expiresOn is null)
        {
            return ShelfLifeStatus.Fine;
        }

        if (expiresOn.Value < asOf)
        {
            return ShelfLifeStatus.Expired;
        }

        return rule.WarnWithinDays is { } days && expiresOn.Value <= asOf.AddDays(days)
            ? ShelfLifeStatus.Short
            : ShelfLifeStatus.Fine;
    }
}

/// <summary>
/// A kind of thing that is donated. A fixed category carries what customs and the carrier need to know about
/// it: its hazard class, whether it is sensitive or not carried at all, how soon it counts as short-dated and a
/// customs code per authority (ADR 0014, D6). The Administrator maintains the mapping (O31).
/// </summary>
public class ItemCategory
{
    public int Id { get; set; }

    public required string NameEn { get; set; }

    /// <summary>Empty until a translation is supplied; plan 16 prints it on the bilingual label.</summary>
    public string NameUk { get; set; } = string.Empty;

    public bool IsFixed { get; set; }

    /// <summary>The ADR hazard class (1 to 9), or <see langword="null"/> for goods that are not dangerous.</summary>
    public int? HazardClass { get; set; }

    public bool IsSensitive { get; set; }

    /// <summary>Not carried on a convoy at all: adding one warns at once (D21).</summary>
    public bool IsNotCarried { get; set; }

    public ShelfLifeRule ShelfLife { get; set; } = ShelfLifeRule.None;

    public Dictionary<CustomsAuthority, string> CustomsCodes { get; set; } = [];
}

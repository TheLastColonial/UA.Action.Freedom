namespace UA.Action.Freedom.Application.Donations;

/// <summary>
/// A donor as this slice persists and returns it: the anonymous key joined to the erasable detail.
/// </summary>
/// <remarks>
/// Personal data (ADR 0013): never written to a log. A donor who has been erased is not readable at all; where
/// a record still names one, it reads <see cref="FormerDonor"/>.
/// </remarks>
public sealed record DonorReadModel(
    Guid Id,
    string Name,
    string? Email,
    string? Phone,
    string? LastChangedByName = null,
    DateTime? LastChangedAt = null);

/// <summary>One donor's drop-off. <see cref="DonorName"/> reads "Former donor" once the donor is erased.</summary>
public sealed record DonationReadModel(
    int Id,
    Guid DonorId,
    string DonorName,
    DateOnly ReceivedOn,
    string? Notes,
    string? LastChangedByName = null,
    DateTime? LastChangedAt = null);

public static class DonorNames
{
    public const string FormerDonor = "Former donor";
}

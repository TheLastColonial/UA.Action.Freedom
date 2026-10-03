namespace UA.Action.Freedom.Domain;

/// <summary>
/// Someone who gave goods to Ukrainian Action. A donor has no login: a Dispatcher or Loader enters them.
/// </summary>
/// <remarks>
/// A split identity like <see cref="Person"/> (ADR 0013). The <see cref="DonorId"/> is the anonymous key a
/// donation points at; the personal data is <see cref="DonorDetail"/>, which erasure deletes. A donor is
/// never the same record as a volunteer, so erasing one never erases the other.
/// </remarks>
public class Donor
{
    public required DonorId Id { get; set; }
}

/// <summary>Unique reference to a <see cref="Donor"/>. A <see cref="Guid"/> so a URL does not count the donors.</summary>
public record DonorId(Guid Value);

/// <summary>A donor's personal data — the erasable half of the split identity.</summary>
public class DonorDetail
{
    public required string Name { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }
}

/// <summary>One donor, one drop-off, many items. Items belong to a donation, so each is attributed to its donor.</summary>
public class Donation
{
    public required DonationId Id { get; set; }

    public required DonorId DonorId { get; set; }

    public DateOnly ReceivedOn { get; set; }

    public string? Notes { get; set; }
}

/// <summary>Unique reference to a <see cref="Donation"/>.</summary>
public record DonationId(int Value);

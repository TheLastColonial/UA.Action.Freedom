namespace UA.Action.Freedom.Application.Donations;

/// <summary>Persistence port for <see cref="DonationReadModel"/>.</summary>
public interface IDonationRepository
{
    Task<DonationReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>A page of donations, newest first. <paramref name="donorId"/> narrows it to one donor's.</summary>
    Task<IReadOnlyList<DonationReadModel>> ListAsync(
        Guid? donorId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Records the donation and returns the identifier the database assigned. The donor must exist.</summary>
    Task<int> AddAsync(Guid donorId, DateOnly receivedOn, string? notes, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(int id, DateOnly receivedOn, string? notes, CancellationToken cancellationToken);

    Task<DeleteDonationResult> DeleteAsync(int id, CancellationToken cancellationToken);

    /// <summary>The donor name as a record shows it, Former donor once erased, or nothing if there never was one.</summary>
    Task<string?> DonorNameAsync(Guid donorId, CancellationToken cancellationToken);

    /// <summary>Every item the donor gave, with what each is worth and the status of the box it is in.</summary>
    Task<IReadOnlyList<DonorReportItem>> ReportItemsAsync(Guid donorId, CancellationToken cancellationToken);
}

public enum DeleteDonationResult
{
    Deleted,
    NotFound,
    StillReferenced
}

/// <summary>
/// One row of the donor status report: deliberately only what a donor may be told. There is no receiver, region,
/// route or address on it, and no way to carry one.
/// </summary>
public sealed record DonorReportItem(
    int DonationId,
    DateOnly ReceivedOn,
    string CategoryNameEn,
    int Quantity,
    decimal? ValueGbp,
    bool BoxValidated);

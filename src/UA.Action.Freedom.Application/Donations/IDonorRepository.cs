namespace UA.Action.Freedom.Application.Donations;

/// <summary>Persistence port for <see cref="DonorReadModel"/> over the split donor identity.</summary>
public interface IDonorRepository
{
    Task<DonorReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<DonorReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task AddAsync(DonorReadModel donor, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(DonorReadModel donor, CancellationToken cancellationToken);

    /// <summary>
    /// Erases the donor: their personal data is deleted. A donation still names the anonymous key, which is kept
    /// and stamped erased, so the donation, its items and its value survive and read "Former donor". Never refused
    /// for being in use: a donor has no operational dependency (ADR 0013).
    /// </summary>
    Task<bool> EraseAsync(Guid id, CancellationToken cancellationToken);
}

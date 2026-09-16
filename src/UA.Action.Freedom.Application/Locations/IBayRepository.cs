namespace UA.Action.Freedom.Application.Locations;

/// <summary>
/// Persistence port for <see cref="BayReadModel"/>.
/// </summary>
public interface IBayRepository
{
    Task<BayReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Bays belonging to one location, ordered by code.</summary>
    Task<IReadOnlyList<BayReadModel>> ListByLocationAsync(int locationId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a bay with this code already exists within the location, other than
    /// <paramref name="excludeId"/> itself (so an update checking against its own code does not
    /// conflict with itself).
    /// </summary>
    Task<bool> CodeExistsAsync(int locationId, string code, int? excludeId, CancellationToken cancellationToken);

    /// <summary>Inserts a bay and returns the identifier the database assigned.</summary>
    Task<int> AddAsync(BayReadModel bay, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(BayReadModel bay, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
}

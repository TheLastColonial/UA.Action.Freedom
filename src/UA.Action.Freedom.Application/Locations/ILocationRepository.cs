namespace UA.Action.Freedom.Application.Locations;

/// <summary>
/// Persistence port for <see cref="LocationReadModel"/>.
/// </summary>
public interface ILocationRepository
{
    Task<LocationReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<LocationReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken);

    /// <summary>Inserts a location and returns the identifier the database assigned.</summary>
    Task<int> AddAsync(LocationReadModel location, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(LocationReadModel location, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
}

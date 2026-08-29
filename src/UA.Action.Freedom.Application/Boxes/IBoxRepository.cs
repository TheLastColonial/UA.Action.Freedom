namespace UA.Action.Freedom.Application.Boxes;

/// <summary>
/// Persistence port for <see cref="BoxReadModel"/> and the items inside a box.
/// </summary>
public interface IBoxRepository
{
    Task<BoxReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<BoxReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken);

    /// <summary>Inserts a box and returns the identifier the database assigned.</summary>
    Task<int> AddAsync(BoxReadModel box, CancellationToken cancellationToken);

    /// <summary>
    /// Updates location and receiver. Deliberately cannot touch the weight or the validation
    /// record — those come from <see cref="ValidateAsync"/> and nowhere else.
    /// </summary>
    Task<bool> UpdateAsync(BoxReadModel box, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Records the confirmed weight and who checked it, but only if the box is not already
    /// validated. Returns false when the box does not exist <em>or</em> was already validated —
    /// the caller distinguishes those by reading the box.
    /// </summary>
    Task<bool> ValidateAsync(
        int id, Guid validatedByPersonId, int weightKg, DateTime validatedAt, CancellationToken cancellationToken);

    Task<IReadOnlyList<BoxItemReadModel>> ListItemsAsync(int boxId, CancellationToken cancellationToken);

    Task AddItemAsync(int boxId, BoxItemReadModel item, CancellationToken cancellationToken);

    Task<bool> DeleteItemAsync(int boxId, Guid itemId, CancellationToken cancellationToken);
}

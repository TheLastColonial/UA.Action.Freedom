namespace UA.Action.Freedom.Application.People;

/// <summary>
/// Persistence port for <see cref="PersonReadModel"/>. Implemented in the Data project; the
/// handlers here depend only on this. The write methods report whether a row was affected so
/// handlers can distinguish "not found" from "done" without a prior read.
/// </summary>
public interface IPersonRepository
{
    Task<PersonReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// A page of volunteers ordered by name. <paramref name="driversOnly"/> narrows it to those
    /// who drive, which is the list a dispatcher builds driver teams from.
    /// </summary>
    Task<IReadOnlyList<PersonReadModel>> ListAsync(int page, int pageSize, bool driversOnly, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task AddAsync(PersonReadModel person, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(PersonReadModel person, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

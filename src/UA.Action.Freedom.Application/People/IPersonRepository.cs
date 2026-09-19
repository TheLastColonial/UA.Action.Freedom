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

    /// <summary>
    /// Removes the volunteer, unless something still names them — a vehicle crew, a manifest
    /// team, or the record of who validated or shelved a box. Those records are the charity's
    /// account of who did what, so they keep the volunteer rather than lose the name.
    /// </summary>
    Task<DeletePersonResult> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public enum DeletePersonResult
{
    Deleted,
    NotFound,
    StillReferenced
}

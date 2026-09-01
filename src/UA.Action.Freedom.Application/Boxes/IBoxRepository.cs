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

    /// <summary>The code a scan of this box currently resolves to, or <c>null</c> if it has none.</summary>
    Task<BoxQrCodeReadModel?> GetActiveQrCodeAsync(int boxId, CancellationToken cancellationToken);

    /// <summary>
    /// The box's active code for <paramref name="token"/>, or <c>null</c> if no active code
    /// carries that token — the token is unknown, or it names a code that has since been revoked.
    /// </summary>
    Task<BoxQrCodeReadModel?> ResolveActiveQrCodeAsync(Guid token, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes any code the box currently has and issues <paramref name="token"/> in its place,
    /// as one act — the old label stops resolving at the instant the new one starts. The two
    /// statements run in a transaction for that reason.
    /// </summary>
    Task<BoxQrCodeReadModel> IssueQrCodeAsync(
        int boxId, Guid token, DateTime issuedAt, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the box's active code without issuing another. Returns false when the box has no
    /// active code (whether or not the box itself exists).
    /// </summary>
    Task<bool> RevokeActiveQrCodeAsync(int boxId, CancellationToken cancellationToken);
}

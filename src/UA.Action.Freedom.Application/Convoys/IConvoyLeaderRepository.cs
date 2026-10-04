namespace UA.Action.Freedom.Application.Convoys;

/// <summary>One stretch of a volunteer leading a convoy. <see cref="Until"/> is null while it is open.</summary>
public sealed record ConvoyLeaderAssignmentReadModel(
    int Id,
    int ConvoyId,
    Guid PersonId,
    string PersonName,
    DateTime From,
    DateTime? Until,
    string? LastChangedByName = null,
    DateTime? LastChangedAt = null);

/// <summary>The convoy leader now, and everyone who has led the convoy, newest first.</summary>
public sealed record ConvoyLeaderReadModel(
    ConvoyLeaderAssignmentReadModel? Current,
    IReadOnlyList<ConvoyLeaderAssignmentReadModel> History);

public enum NominateLeaderResult
{
    Nominated,
    AlreadyLeader,
    NotADriverOnConvoy
}

/// <summary>Persistence port for <c>dbo.ConvoyLeaderAssignment</c>.</summary>
public interface IConvoyLeaderRepository
{
    /// <summary>Every assignment of the convoy, newest first.</summary>
    Task<IReadOnlyList<ConvoyLeaderAssignmentReadModel>> HistoryAsync(int convoyId, CancellationToken cancellationToken);

    /// <summary>
    /// Closes the open assignment and opens one for <paramref name="personId"/> in one transaction, provided that person is
    /// a Driver on the convoy's crew and is not already the leader.
    /// </summary>
    Task<NominateLeaderResult> NominateAsync(int convoyId, Guid personId, DateTime at, CancellationToken cancellationToken);

    Task<bool> IsCurrentLeaderAsync(int convoyId, Guid personId, CancellationToken cancellationToken);
}

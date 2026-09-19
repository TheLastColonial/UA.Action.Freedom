namespace UA.Action.Freedom.Application.Abstractions;

/// <summary>
/// What a repository delete found. <see cref="StillReferenced"/> is the database refusing because
/// another record — a manifest, most often — still names the row: those records are the account of
/// what happened and they keep what they name.
/// </summary>
public enum DeleteResult
{
    Deleted,
    NotFound,
    StillReferenced
}

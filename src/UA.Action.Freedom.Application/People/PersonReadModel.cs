namespace UA.Action.Freedom.Application.People;

/// <summary>
/// A volunteer as this slice persists and returns it.
/// </summary>
/// <remarks>
/// A flat projection of <see cref="Domain.Person"/> and <see cref="Domain.Driver"/>: the domain
/// models a driver as a subtype, while the database holds one row per volunteer with
/// <see cref="IsDriver"/> distinguishing them. Keeping the read side flat is what lets Dapper
/// hydrate a row by constructor, so the CLR types here must line up with the column types in
/// <c>dbo.Person</c> exactly.
///
/// This is personal data (docs/recommendations.md §4.8): UK residency, a defined retention
/// period, and never written to a log.
/// </remarks>
public sealed record PersonReadModel(
    Guid Id,
    string FirstName,
    string LastName,
    DateTime DateOfBirth,
    DateTime Joined,
    string? Phone,
    bool IsDriver,
    bool Committed);

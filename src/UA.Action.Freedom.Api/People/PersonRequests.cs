using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Api.People;

/// <summary>
/// Body of <c>POST /people</c>. The identifier is minted by the application, not supplied here.
/// </summary>
public sealed record CreatePersonRequest(
    string FirstName,
    string LastName,
    DateTime DateOfBirth,
    DateTime Joined,
    string? Phone,
    bool IsDriver,
    bool Committed)
{
    public CreatePersonCommand ToCommand() => new(
        FirstName, LastName, DateOfBirth, Joined, Phone, IsDriver, Committed);
}

/// <summary>Body of <c>PUT /people/{id}</c>. The route supplies the identifier.</summary>
public sealed record UpdatePersonRequest(
    string FirstName,
    string LastName,
    DateTime DateOfBirth,
    DateTime Joined,
    string? Phone,
    bool IsDriver,
    bool Committed)
{
    public UpdatePersonCommand ToCommand(Guid id) => new(
        id, FirstName, LastName, DateOfBirth, Joined, Phone, IsDriver, Committed);
}

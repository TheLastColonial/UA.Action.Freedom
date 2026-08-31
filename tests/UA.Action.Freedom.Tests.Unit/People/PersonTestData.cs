using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Unit.People;

/// <summary>
/// Factory for volunteer test data. Every field has a sensible default; a test overrides only
/// what it is actually about.
/// </summary>
internal static class PersonTestData
{
    internal static readonly Guid Id = new("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

    internal static CreatePersonCommand ACreateCommand(
        string firstName = "Olena",
        string lastName = "Shevchenko",
        bool isDriver = false,
        bool committed = false) => new(
        FirstName: firstName,
        LastName: lastName,
        DateOfBirth: new DateTime(1988, 4, 12, 0, 0, 0, DateTimeKind.Utc),
        Joined: new DateTime(2024, 2, 24, 0, 0, 0, DateTimeKind.Utc),
        Phone: "+447700900123",
        IsDriver: isDriver,
        Committed: committed);

    internal static UpdatePersonCommand AnUpdateCommand(
        Guid? id = null,
        string lastName = "Shevchenko-Bell",
        bool isDriver = true,
        bool committed = true) => new(
        Id: id ?? Id,
        FirstName: "Olena",
        LastName: lastName,
        DateOfBirth: new DateTime(1988, 4, 12, 0, 0, 0, DateTimeKind.Utc),
        Joined: new DateTime(2024, 2, 24, 0, 0, 0, DateTimeKind.Utc),
        Phone: "+447700900123",
        IsDriver: isDriver,
        Committed: committed);

    internal static PersonReadModel AReadModel(Guid? id = null, bool isDriver = false) => new(
        Id: id ?? Id,
        FirstName: "Olena",
        LastName: "Shevchenko",
        DateOfBirth: new DateTime(1988, 4, 12, 0, 0, 0, DateTimeKind.Utc),
        Joined: new DateTime(2024, 2, 24, 0, 0, 0, DateTimeKind.Utc),
        Phone: "+447700900123",
        IsDriver: isDriver,
        Committed: false);
}

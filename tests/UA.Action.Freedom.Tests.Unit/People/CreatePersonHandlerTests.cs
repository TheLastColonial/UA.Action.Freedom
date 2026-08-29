using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Unit.People;

/// <summary>
/// Recording a volunteer. There is no natural key for a person — two volunteers may genuinely
/// share a name — so creation always succeeds and the handler mints the identifier.
/// </summary>
/// <remarks>
/// The identifier is a <see cref="Guid"/> generated here rather than a sequence handed back by
/// the database: volunteer records are personal data, and an id that says how many volunteers
/// there are is an id that leaks something in every URL it appears in (recommendations §4.8).
/// </remarks>
public class CreatePersonHandlerTests
{
    [Fact]
    public async Task Persists_the_volunteer_and_returns_the_identifier_it_minted()
    {
        var repository = Substitute.For<IPersonRepository>();
        var handler = new CreatePersonHandler(repository);

        var id = await handler.HandleAsync(PersonTestData.ACreateCommand(), CancellationToken.None);

        id.Should().NotBeEmpty();
        await repository.Received(1).AddAsync(
            Arg.Is<PersonReadModel>(person =>
                person.Id == id
                && person.FirstName == "Olena"
                && person.LastName == "Shevchenko"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mints_a_different_identifier_for_every_volunteer()
    {
        var repository = Substitute.For<IPersonRepository>();
        var handler = new CreatePersonHandler(repository);

        var first = await handler.HandleAsync(PersonTestData.ACreateCommand(), CancellationToken.None);
        var second = await handler.HandleAsync(PersonTestData.ACreateCommand(), CancellationToken.None);

        second.Should().NotBe(first);
    }

    [Fact]
    public async Task Records_a_driver_as_a_driver()
    {
        var repository = Substitute.For<IPersonRepository>();
        var handler = new CreatePersonHandler(repository);

        await handler.HandleAsync(
            PersonTestData.ACreateCommand(isDriver: true, committed: true),
            CancellationToken.None);

        await repository.Received(1).AddAsync(
            Arg.Is<PersonReadModel>(person => person.IsDriver && person.Committed),
            Arg.Any<CancellationToken>());
    }
}

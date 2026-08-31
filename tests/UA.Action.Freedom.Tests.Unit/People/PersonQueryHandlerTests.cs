using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Unit.People;

/// <summary>
/// Reading volunteers. The list handler is where paging is clamped and where the
/// drivers-only filter is resolved, so both are pinned by the call that reaches the repository.
/// </summary>
public class PersonQueryHandlerTests
{
    [Fact]
    public async Task Get_returns_the_person_when_one_exists()
    {
        var repository = Substitute.For<IPersonRepository>();
        repository.GetByIdAsync(PersonTestData.Id, Arg.Any<CancellationToken>())
            .Returns(PersonTestData.AReadModel());
        var handler = new GetPersonByIdHandler(repository);

        var person = await handler.HandleAsync(new GetPersonByIdQuery(PersonTestData.Id), CancellationToken.None);

        person.Should().NotBeNull();
        person!.LastName.Should().Be("Shevchenko");
    }

    [Fact]
    public async Task Get_returns_nothing_when_there_is_no_such_person()
    {
        var repository = Substitute.For<IPersonRepository>();
        repository.GetByIdAsync(PersonTestData.Id, Arg.Any<CancellationToken>())
            .Returns((PersonReadModel?)null);
        var handler = new GetPersonByIdHandler(repository);

        var person = await handler.HandleAsync(new GetPersonByIdQuery(PersonTestData.Id), CancellationToken.None);

        person.Should().BeNull();
    }

    [Fact]
    public async Task List_passes_the_requested_page_through_unchanged()
    {
        var repository = Substitute.For<IPersonRepository>();
        repository.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<PersonReadModel> { PersonTestData.AReadModel() });
        var handler = new ListPeopleHandler(repository);

        await handler.HandleAsync(new ListPeopleQuery(Page: 3, PageSize: 25, DriversOnly: false), CancellationToken.None);

        await repository.Received(1).ListAsync(3, 25, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_clamps_a_nonsense_page_and_page_size_to_the_defaults()
    {
        var repository = Substitute.For<IPersonRepository>();
        repository.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<PersonReadModel> { PersonTestData.AReadModel() });
        var handler = new ListPeopleHandler(repository);

        await handler.HandleAsync(new ListPeopleQuery(Page: 0, PageSize: 100_000, DriversOnly: false), CancellationToken.None);

        await repository.Received(1).ListAsync(1, 50, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_asks_only_for_drivers_when_that_is_what_was_requested()
    {
        // Building a driver team means picking from drivers, not from every volunteer on file.
        var repository = Substitute.For<IPersonRepository>();
        repository.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<PersonReadModel> { PersonTestData.AReadModel(isDriver: true) });
        var handler = new ListPeopleHandler(repository);

        await handler.HandleAsync(new ListPeopleQuery(Page: 1, PageSize: 50, DriversOnly: true), CancellationToken.None);

        await repository.Received(1).ListAsync(1, 50, true, Arg.Any<CancellationToken>());
    }
}

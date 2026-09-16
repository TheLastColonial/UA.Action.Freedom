using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Locations;

namespace UA.Action.Freedom.Tests.Unit.Locations;

/// <summary>
/// Distribution hubs: plain CRUD, no lifecycle. The only thing worth pinning is that reads and
/// writes report "not found" rather than throwing when the id does not exist.
/// </summary>
public class LocationHandlerTests
{
    [Fact]
    public async Task Creating_a_location_hands_back_the_identifier_the_repository_assigned()
    {
        var repository = Substitute.For<ILocationRepository>();
        repository.AddAsync(Arg.Any<LocationReadModel>(), Arg.Any<CancellationToken>()).Returns(3);
        var handler = new CreateLocationHandler(repository);

        var id = await handler.HandleAsync(LocationTestData.ACreateLocationCommand(), CancellationToken.None);

        id.Should().Be(3);
        await repository.Received(1).AddAsync(
            Arg.Is<LocationReadModel>(location => location.Name == "Coventry Depot"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Updating_a_location_that_exists_reports_it_updated()
    {
        var repository = Substitute.For<ILocationRepository>();
        repository.UpdateAsync(Arg.Any<LocationReadModel>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new UpdateLocationHandler(repository);

        var outcome = await handler.HandleAsync(LocationTestData.AnUpdateLocationCommand(), CancellationToken.None);

        outcome.Should().Be(UpdateLocationOutcome.Updated);
    }

    [Fact]
    public async Task Updating_a_location_that_does_not_exist_reports_not_found()
    {
        var repository = Substitute.For<ILocationRepository>();
        repository.UpdateAsync(Arg.Any<LocationReadModel>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = new UpdateLocationHandler(repository);

        var outcome = await handler.HandleAsync(LocationTestData.AnUpdateLocationCommand(), CancellationToken.None);

        outcome.Should().Be(UpdateLocationOutcome.NotFound);
    }

    [Fact]
    public async Task Deleting_a_location_that_exists_reports_it_deleted()
    {
        var repository = Substitute.For<ILocationRepository>();
        repository.DeleteAsync(3, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new DeleteLocationHandler(repository);

        var outcome = await handler.HandleAsync(new DeleteLocationCommand(3), CancellationToken.None);

        outcome.Should().Be(DeleteLocationOutcome.Deleted);
    }

    [Fact]
    public async Task Deleting_a_location_that_does_not_exist_reports_not_found()
    {
        var repository = Substitute.For<ILocationRepository>();
        repository.DeleteAsync(3, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new DeleteLocationHandler(repository);

        var outcome = await handler.HandleAsync(new DeleteLocationCommand(3), CancellationToken.None);

        outcome.Should().Be(DeleteLocationOutcome.NotFound);
    }

    [Fact]
    public async Task List_clamps_a_nonsense_page_and_page_size_to_the_defaults()
    {
        var repository = Substitute.For<ILocationRepository>();
        repository.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<LocationReadModel>());
        var handler = new ListLocationsHandler(repository);

        await handler.HandleAsync(new ListLocationsQuery(Page: 0, PageSize: 100_000), CancellationToken.None);

        await repository.Received(1).ListAsync(1, 50, Arg.Any<CancellationToken>());
    }
}

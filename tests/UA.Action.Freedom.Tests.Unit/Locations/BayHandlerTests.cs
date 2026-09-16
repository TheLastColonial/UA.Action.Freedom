using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Locations;

namespace UA.Action.Freedom.Tests.Unit.Locations;

/// <summary>
/// Bays within a location. The rules worth a handler: a bay cannot be added to a location that
/// does not exist, and its code only has to be distinct within its own location.
/// </summary>
public class BayHandlerTests
{
    [Fact]
    public async Task Creating_a_bay_at_a_known_location_with_a_free_code_succeeds()
    {
        var bays = Substitute.For<IBayRepository>();
        var locations = Substitute.For<ILocationRepository>();
        locations.ExistsAsync(3, Arg.Any<CancellationToken>()).Returns(true);
        bays.CodeExistsAsync(3, "A1", null, Arg.Any<CancellationToken>()).Returns(false);
        bays.AddAsync(Arg.Any<BayReadModel>(), Arg.Any<CancellationToken>()).Returns(9);
        var handler = new CreateBayHandler(bays, locations);

        var result = await handler.HandleAsync(LocationTestData.ACreateBayCommand(), CancellationToken.None);

        result.Outcome.Should().Be(CreateBayOutcome.Created);
        result.Id.Should().Be(9);
    }

    [Fact]
    public async Task Creating_a_bay_at_an_unknown_location_is_refused()
    {
        var bays = Substitute.For<IBayRepository>();
        var locations = Substitute.For<ILocationRepository>();
        locations.ExistsAsync(3, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new CreateBayHandler(bays, locations);

        var result = await handler.HandleAsync(LocationTestData.ACreateBayCommand(), CancellationToken.None);

        result.Outcome.Should().Be(CreateBayOutcome.LocationNotFound);
        await bays.DidNotReceive().AddAsync(Arg.Any<BayReadModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Two_bays_at_the_same_location_cannot_share_a_code()
    {
        var bays = Substitute.For<IBayRepository>();
        var locations = Substitute.For<ILocationRepository>();
        locations.ExistsAsync(3, Arg.Any<CancellationToken>()).Returns(true);
        bays.CodeExistsAsync(3, "A1", null, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new CreateBayHandler(bays, locations);

        var result = await handler.HandleAsync(LocationTestData.ACreateBayCommand(), CancellationToken.None);

        result.Outcome.Should().Be(CreateBayOutcome.CodeConflict);
        await bays.DidNotReceive().AddAsync(Arg.Any<BayReadModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Two_different_locations_may_each_have_a_bay_with_the_same_code()
    {
        // The code is unique per location, not globally: "A1" at Coventry and "A1" at London are
        // different bays.
        var bays = Substitute.For<IBayRepository>();
        var locations = Substitute.For<ILocationRepository>();
        locations.ExistsAsync(4, Arg.Any<CancellationToken>()).Returns(true);
        bays.CodeExistsAsync(4, "A1", null, Arg.Any<CancellationToken>()).Returns(false);
        bays.AddAsync(Arg.Any<BayReadModel>(), Arg.Any<CancellationToken>()).Returns(10);
        var handler = new CreateBayHandler(bays, locations);

        var result = await handler.HandleAsync(LocationTestData.ACreateBayCommand(locationId: 4), CancellationToken.None);

        result.Outcome.Should().Be(CreateBayOutcome.Created);
    }

    [Fact]
    public async Task Renaming_a_bay_to_a_code_already_used_elsewhere_in_the_location_is_refused()
    {
        var bays = Substitute.For<IBayRepository>();
        bays.CodeExistsAsync(3, "A1", 9, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new UpdateBayHandler(bays);

        var outcome = await handler.HandleAsync(LocationTestData.AnUpdateBayCommand(), CancellationToken.None);

        outcome.Should().Be(UpdateBayOutcome.CodeConflict);
        await bays.DidNotReceive().UpdateAsync(Arg.Any<BayReadModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Renaming_a_bay_that_does_not_exist_reports_not_found()
    {
        var bays = Substitute.For<IBayRepository>();
        bays.CodeExistsAsync(3, "A1", 9, Arg.Any<CancellationToken>()).Returns(false);
        bays.UpdateAsync(Arg.Any<BayReadModel>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = new UpdateBayHandler(bays);

        var outcome = await handler.HandleAsync(LocationTestData.AnUpdateBayCommand(), CancellationToken.None);

        outcome.Should().Be(UpdateBayOutcome.NotFound);
    }

    [Fact]
    public async Task Deleting_a_bay_that_exists_reports_it_deleted()
    {
        var bays = Substitute.For<IBayRepository>();
        bays.DeleteAsync(9, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new DeleteBayHandler(bays);

        var outcome = await handler.HandleAsync(new DeleteBayCommand(9), CancellationToken.None);

        outcome.Should().Be(DeleteBayOutcome.Deleted);
    }

    [Fact]
    public async Task Listing_bays_for_an_unknown_location_returns_nothing()
    {
        var bays = Substitute.For<IBayRepository>();
        var locations = Substitute.For<ILocationRepository>();
        locations.ExistsAsync(3, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new ListBaysHandler(bays, locations);

        var result = await handler.HandleAsync(new ListBaysQuery(3), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Listing_bays_for_a_location_with_none_yet_is_an_empty_list_not_null()
    {
        var bays = Substitute.For<IBayRepository>();
        var locations = Substitute.For<ILocationRepository>();
        locations.ExistsAsync(3, Arg.Any<CancellationToken>()).Returns(true);
        bays.ListByLocationAsync(3, Arg.Any<CancellationToken>()).Returns(new List<BayReadModel>());
        var handler = new ListBaysHandler(bays, locations);

        var result = await handler.HandleAsync(new ListBaysQuery(3), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Should().BeEmpty();
    }
}

using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Vehicles;

namespace UA.Action.Freedom.Tests.Unit.Vehicles;

public class VehicleQueryHandlerTests
{
    [Fact]
    public async Task Get_returns_the_vehicle_the_repository_has()
    {
        var repository = Substitute.For<IVehicleRepository>();
        var stored = VehicleTestData.AReadModel();
        repository.GetByVinAsync("WVWZZZ1JZXW000001", Arg.Any<CancellationToken>()).Returns(stored);
        var handler = new GetVehicleByVinHandler(repository);

        var result = await handler.HandleAsync(new GetVehicleByVinQuery("WVWZZZ1JZXW000001"), CancellationToken.None);

        result.Should().Be(stored);
    }

    [Fact]
    public async Task Get_returns_null_when_the_repository_has_no_such_vehicle()
    {
        var repository = Substitute.For<IVehicleRepository>();
        repository.GetByVinAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((VehicleReadModel?)null);
        var handler = new GetVehicleByVinHandler(repository);

        var result = await handler.HandleAsync(new GetVehicleByVinQuery("UNKNOWNVIN0000001"), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task List_clamps_a_nonsense_page_and_page_size_to_the_defaults()
    {
        var repository = Substitute.For<IVehicleRepository>();
        repository.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<VehicleReadModel> { VehicleTestData.AReadModel() });
        var handler = new ListVehiclesHandler(repository);

        await handler.HandleAsync(new ListVehiclesQuery(Page: 0, PageSize: 100_000), CancellationToken.None);

        await repository.Received(1).ListAsync(1, 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_passes_a_valid_page_request_through_unchanged()
    {
        var repository = Substitute.For<IVehicleRepository>();
        repository.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<VehicleReadModel>());
        var handler = new ListVehiclesHandler(repository);

        await handler.HandleAsync(new ListVehiclesQuery(Page: 3, PageSize: 25), CancellationToken.None);

        await repository.Received(1).ListAsync(3, 25, Arg.Any<CancellationToken>());
    }
}

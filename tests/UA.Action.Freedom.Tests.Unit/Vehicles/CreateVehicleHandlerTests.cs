using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Vehicles;

namespace UA.Action.Freedom.Tests.Unit.Vehicles;

/// <summary>
/// Recording a sourced vehicle. VIN is the key, so the one rule worth a handler is that a
/// second vehicle cannot claim a VIN that is already taken.
/// </summary>
public class CreateVehicleHandlerTests
{
    [Fact]
    public async Task Persists_the_vehicle_and_reports_it_created_when_the_VIN_is_free()
    {
        var repository = Substitute.For<IVehicleRepository>();
        repository.ExistsAsync("WVWZZZ1JZXW000001", Arg.Any<CancellationToken>()).Returns(false);
        var handler = new CreateVehicleHandler(repository);

        var outcome = await handler.HandleAsync(VehicleTestData.ACreateCommand(), CancellationToken.None);

        outcome.Should().Be(CreateVehicleOutcome.Created);
        await repository.Received(1).AddAsync(
            Arg.Is<VehicleReadModel>(v => v.Vin == "WVWZZZ1JZXW000001" && v.Plate == "AB12CDE" && v.WeightKg == 1_400),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_a_conflict_and_does_not_write_when_the_VIN_is_taken()
    {
        var repository = Substitute.For<IVehicleRepository>();
        repository.ExistsAsync("WVWZZZ1JZXW000001", Arg.Any<CancellationToken>()).Returns(true);
        var handler = new CreateVehicleHandler(repository);

        var outcome = await handler.HandleAsync(VehicleTestData.ACreateCommand(), CancellationToken.None);

        outcome.Should().Be(CreateVehicleOutcome.Conflict);
        await repository.DidNotReceive().AddAsync(Arg.Any<VehicleReadModel>(), Arg.Any<CancellationToken>());
    }
}

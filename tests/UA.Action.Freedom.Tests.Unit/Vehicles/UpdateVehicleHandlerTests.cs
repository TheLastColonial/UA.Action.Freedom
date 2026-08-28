using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Vehicles;

namespace UA.Action.Freedom.Tests.Unit.Vehicles;

/// <summary>
/// Updating a vehicle. The handler leans on the repository's affected-row count to tell
/// "changed it" from "no such VIN" rather than reading first.
/// </summary>
public class UpdateVehicleHandlerTests
{
    [Fact]
    public async Task Reports_updated_when_a_row_changed()
    {
        var repository = Substitute.For<IVehicleRepository>();
        repository.UpdateAsync(Arg.Any<VehicleReadModel>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new UpdateVehicleHandler(repository);

        var outcome = await handler.HandleAsync(VehicleTestData.AnUpdateCommand(plate: "ZZ99ZZZ"), CancellationToken.None);

        outcome.Should().Be(UpdateVehicleOutcome.Updated);
        await repository.Received(1).UpdateAsync(
            Arg.Is<VehicleReadModel>(v => v.Vin == "WVWZZZ1JZXW000001" && v.Plate == "ZZ99ZZZ"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_not_found_when_no_row_matched_the_VIN()
    {
        var repository = Substitute.For<IVehicleRepository>();
        repository.UpdateAsync(Arg.Any<VehicleReadModel>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = new UpdateVehicleHandler(repository);

        var outcome = await handler.HandleAsync(VehicleTestData.AnUpdateCommand(vin: "UNKNOWNVIN0000001"), CancellationToken.None);

        outcome.Should().Be(UpdateVehicleOutcome.NotFound);
    }
}

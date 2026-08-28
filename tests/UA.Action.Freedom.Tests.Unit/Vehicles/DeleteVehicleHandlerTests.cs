using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Vehicles;

namespace UA.Action.Freedom.Tests.Unit.Vehicles;

public class DeleteVehicleHandlerTests
{
    [Fact]
    public async Task Reports_deleted_when_a_row_was_removed()
    {
        var repository = Substitute.For<IVehicleRepository>();
        repository.DeleteAsync("WVWZZZ1JZXW000001", Arg.Any<CancellationToken>()).Returns(true);
        var handler = new DeleteVehicleHandler(repository);

        var outcome = await handler.HandleAsync(new DeleteVehicleCommand("WVWZZZ1JZXW000001"), CancellationToken.None);

        outcome.Should().Be(DeleteVehicleOutcome.Deleted);
    }

    [Fact]
    public async Task Reports_not_found_when_there_was_no_such_vehicle()
    {
        var repository = Substitute.For<IVehicleRepository>();
        repository.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = new DeleteVehicleHandler(repository);

        var outcome = await handler.HandleAsync(new DeleteVehicleCommand("UNKNOWNVIN0000001"), CancellationToken.None);

        outcome.Should().Be(DeleteVehicleOutcome.NotFound);
    }
}

using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Vehicles;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Vehicles;

/// <summary>
/// Recording a Mechanic's inspection result. It is its own write, not part of a vehicle edit,
/// so the handler passes exactly the status and notes through and nothing else.
/// </summary>
public class RecordInspectionHandlerTests
{
    [Fact]
    public async Task Records_the_status_and_notes_against_the_vehicle()
    {
        var repository = Substitute.For<IVehicleRepository>();
        repository.RecordInspectionAsync(Arg.Any<string>(), Arg.Any<InspectionStatus>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new RecordInspectionHandler(repository);

        var outcome = await handler.HandleAsync(
            VehicleTestData.ARecordInspectionCommand(status: InspectionStatus.Failed, notes: "Clutch slipping"),
            TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordInspectionOutcome.Recorded);
        await repository.Received(1).RecordInspectionAsync(
            "WVWZZZ1JZXW000001", InspectionStatus.Failed, "Clutch slipping", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_not_found_when_no_vehicle_has_the_VIN()
    {
        var repository = Substitute.For<IVehicleRepository>();
        repository.RecordInspectionAsync(Arg.Any<string>(), Arg.Any<InspectionStatus>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = new RecordInspectionHandler(repository);

        var outcome = await handler.HandleAsync(
            VehicleTestData.ARecordInspectionCommand(vin: "UNKNOWNVIN0000001"), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordInspectionOutcome.NotFound);
    }

    [Fact]
    public async Task Blank_notes_are_stored_as_no_notes()
    {
        var repository = Substitute.For<IVehicleRepository>();
        repository.RecordInspectionAsync(Arg.Any<string>(), Arg.Any<InspectionStatus>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new RecordInspectionHandler(repository);

        await handler.HandleAsync(VehicleTestData.ARecordInspectionCommand(notes: "   "), TestContext.Current.CancellationToken);

        await repository.Received(1).RecordInspectionAsync(
            Arg.Any<string>(), Arg.Any<InspectionStatus>(), Arg.Is<string?>(notes => notes == null), Arg.Any<CancellationToken>());
    }
}

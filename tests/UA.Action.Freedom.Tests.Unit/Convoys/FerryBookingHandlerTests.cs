using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// Recording a vehicle's outbound ferry booking (P1). Whether the vehicle is travelling with the
/// convoy is settled by the write; the handler only tells "no such convoy" and "already arrived" apart.
/// </summary>
public class FerryBookingHandlerTests
{
    private const string Vin = "WVWZZZ1JZXW000001";

    private static FerryBookingRecord ABooking() => new(
        ConvoyTestData.Id, Vin, "P&O Ferries", "POF-48213", new DateTime(2026, 9, 2, 7, 30, 0, DateTimeKind.Utc),
        "Freight, 2 occupants", 310.00m);

    private static (IConvoyRepository Convoys, IConvoyVehicleRepository TruckList) Repositories(ConvoyReadModel? convoy)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(convoy);
        return (convoys, Substitute.For<IConvoyVehicleRepository>());
    }

    [Theory]
    [InlineData(true, RecordFerryBookingOutcome.Recorded)]
    [InlineData(false, RecordFerryBookingOutcome.VehicleNotOnConvoy)]
    public async Task Reports_what_the_write_found(bool written, RecordFerryBookingOutcome expected)
    {
        var (convoys, truckList) = Repositories(ConvoyTestData.AReadModel());
        truckList.RecordFerryBookingAsync(ABooking(), Arg.Any<CancellationToken>()).Returns(written);

        var outcome = await new RecordFerryBookingHandler(convoys, truckList).HandleAsync(
            new RecordFerryBookingCommand(ABooking()), TestContext.Current.CancellationToken);

        outcome.Should().Be(expected);
    }

    [Fact]
    public async Task Reports_an_unknown_convoy_without_writing()
    {
        var (convoys, truckList) = Repositories(convoy: null);

        var outcome = await new RecordFerryBookingHandler(convoys, truckList).HandleAsync(
            new RecordFerryBookingCommand(ABooking()), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordFerryBookingOutcome.ConvoyNotFound);
        await truckList.DidNotReceive().RecordFerryBookingAsync(
            Arg.Any<FerryBookingRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_to_book_a_convoy_that_has_arrived()
    {
        var (convoys, truckList) = Repositories(ConvoyTestData.AnArrivedConvoy());

        var outcome = await new RecordFerryBookingHandler(convoys, truckList).HandleAsync(
            new RecordFerryBookingCommand(ABooking()), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordFerryBookingOutcome.ConvoyArrived);
        await truckList.DidNotReceive().RecordFerryBookingAsync(
            Arg.Any<FerryBookingRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Removes_a_booking_unless_the_convoy_has_arrived()
    {
        var (convoys, truckList) = Repositories(ConvoyTestData.AReadModel());
        truckList.RemoveFerryBookingAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>()).Returns(true);

        var removed = await new RemoveFerryBookingHandler(convoys, truckList).HandleAsync(
            new RemoveFerryBookingCommand(ConvoyTestData.Id, Vin), TestContext.Current.CancellationToken);

        removed.Should().Be(RemoveFerryBookingOutcome.Removed);

        var (arrivedConvoys, arrivedTruckList) = Repositories(ConvoyTestData.AnArrivedConvoy());
        var refused = await new RemoveFerryBookingHandler(arrivedConvoys, arrivedTruckList).HandleAsync(
            new RemoveFerryBookingCommand(ConvoyTestData.Id, Vin), TestContext.Current.CancellationToken);

        refused.Should().Be(RemoveFerryBookingOutcome.ConvoyArrived);
    }

    [Fact]
    public async Task Removing_a_booking_that_was_never_made_is_not_found()
    {
        var (convoys, truckList) = Repositories(ConvoyTestData.AReadModel());

        var outcome = await new RemoveFerryBookingHandler(convoys, truckList).HandleAsync(
            new RemoveFerryBookingCommand(ConvoyTestData.Id, Vin), TestContext.Current.CancellationToken);

        outcome.Should().Be(RemoveFerryBookingOutcome.NotFound);
    }
}

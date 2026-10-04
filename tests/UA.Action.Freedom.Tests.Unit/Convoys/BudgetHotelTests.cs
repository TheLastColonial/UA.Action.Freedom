using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// The Hotel line of the budget reads the cost held on each accommodation booking, so it is never entered twice (O12).
/// A cancelled booking is not counted: whatever it cost is settled by cancelling it, and a fee that stayed is an
/// "Other" cost.
/// </summary>
public class BudgetHotelTests
{
    private static AccommodationBookingReadModel ABooking(int id, decimal? cost, bool cancelled = false) => new(
        id, ConvoyTestData.Id, 7, "Ibis", null, ConvoyTestData.Start, ConvoyTestData.Start.AddDays(1), null, cost,
        cancelled, []);

    private static async Task<BudgetSummaryReadModel?> SummaryAsync(params AccommodationBookingReadModel[] bookings)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(ConvoyTestData.AReadModel());
        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.ListAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([]);
        var budget = Substitute.For<IConvoyBudgetRepository>();
        budget.ListLinesAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([new BudgetLineReadModel(CostType.Hotel, 200m)]);
        budget.ListCostsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([]);
        var equipment = Substitute.For<IVehicleEquipmentRepository>();
        equipment.ListForConvoyAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([]);
        var accommodation = Substitute.For<IAccommodationRepository>();
        accommodation.ListBookingsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(bookings);

        return await new GetBudgetSummaryHandler(convoys, truckList, budget, equipment, accommodation).HandleAsync(
            new GetBudgetSummaryQuery(ConvoyTestData.Id), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Hotel_is_the_sum_of_the_costs_on_the_standing_bookings()
    {
        var summary = await SummaryAsync(ABooking(1, 120m), ABooking(2, 95.50m), ABooking(3, null));

        var hotel = summary!.Lines.Single(line => line.Type == CostType.Hotel);
        hotel.ActualGbp.Should().Be(215.50m);
        hotel.OverBudget.Should().BeTrue();
        summary.ActualTotalGbp.Should().Be(215.50m);
    }

    [Fact]
    public async Task A_cancelled_booking_costs_nothing_here()
    {
        var summary = await SummaryAsync(ABooking(1, 120m), ABooking(2, 300m, cancelled: true));

        summary!.Lines.Single(line => line.Type == CostType.Hotel).ActualGbp.Should().Be(120m);
    }
}

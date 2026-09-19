using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// Whether a convoy is ready to travel. Advisory only — nothing is blocked by it. A vehicle is
/// ready with two drivers and insurance in cover for the departure date; a convoy is ready when
/// it has a route and every vehicle on it is ready.
/// </summary>
public class ConvoyReadinessTests
{
    private static readonly DateTime Departs = ConvoyTestData.Start;

    private static ConvoyVehicleReadModel AVehicle(string vin = "VIN-1", int drivers = 2, int passengers = 0) =>
        new(vin, "PL-" + vin[^1], 1_800, drivers, passengers);

    private static VehicleInsuranceReadModel APolicy(
        string vin = "VIN-1", int startOffset = -7, int endOffset = 30, DateTime? voidedAt = null) => new(
        ConvoyTestData.Id, vin, "Ukraine Aid Mutual", "POL-1",
        Departs.Date.AddDays(startOffset), Departs.Date.AddDays(endOffset), null, "sub", Departs.AddDays(-10), voidedAt);

    private static ConvoyReadinessReadModel Assess(
        bool routePlanned, params (ConvoyVehicleReadModel Vehicle, VehicleInsuranceReadModel? Policy)[] vehicles) =>
        ConvoyReadiness.Assess(Departs, routePlanned, vehicles);

    [Fact]
    public void A_convoy_with_a_route_and_every_vehicle_crewed_and_insured_is_ready()
    {
        var readiness = Assess(routePlanned: true, (AVehicle(), APolicy()));

        readiness.Ready.Should().BeTrue();
        readiness.Reasons.Should().BeEmpty();
        readiness.Vehicles.Single().Ready.Should().BeTrue();
    }

    [Fact]
    public void Passengers_do_not_count_towards_the_two_drivers()
    {
        var readiness = Assess(routePlanned: true, (AVehicle(drivers: 1, passengers: 3), APolicy()));

        var vehicle = readiness.Vehicles.Single();
        vehicle.Ready.Should().BeFalse();
        vehicle.Reasons.Should().Equal("Fewer than two drivers");
        readiness.Ready.Should().BeFalse();
    }

    public static TheoryData<string, VehicleInsuranceReadModel?> UninsuredCases => new()
    {
        { "Insurance not recorded", null },
        { "Insurance voided by a crew change", APolicy(voidedAt: Departs.AddDays(-1)) },
        { "Insurance does not cover the departure date", APolicy(startOffset: 1) },
        { "Insurance does not cover the departure date", APolicy(endOffset: -1) },
    };

    [Theory]
    [MemberData(nameof(UninsuredCases))]
    public void A_vehicle_without_insurance_for_the_departure_date_is_not_ready(
        string reason, VehicleInsuranceReadModel? policy)
    {
        var vehicle = Assess(routePlanned: true, (AVehicle(), policy)).Vehicles.Single();

        vehicle.Insured.Should().BeFalse();
        vehicle.Reasons.Should().Equal(reason);
    }

    [Fact]
    public void Every_reason_a_vehicle_is_not_ready_is_listed()
    {
        var vehicle = Assess(routePlanned: true, (AVehicle(drivers: 0), null)).Vehicles.Single();

        vehicle.Reasons.Should().Equal("Fewer than two drivers", "Insurance not recorded");
    }

    [Fact]
    public void A_convoy_with_no_route_is_not_ready_even_if_every_vehicle_is()
    {
        var readiness = Assess(routePlanned: false, (AVehicle(), APolicy()));

        readiness.Ready.Should().BeFalse();
        readiness.RoutePlanned.Should().BeFalse();
        readiness.Reasons.Should().Equal("No route planned");
    }

    [Fact]
    public void A_convoy_with_no_vehicles_is_not_ready()
    {
        var readiness = Assess(routePlanned: true);

        readiness.Ready.Should().BeFalse();
        readiness.Reasons.Should().Equal("No vehicles on the truck list");
    }

    [Fact]
    public void One_unready_vehicle_makes_the_convoy_not_ready()
    {
        var readiness = Assess(
            routePlanned: true,
            (AVehicle("VIN-1"), APolicy("VIN-1")),
            (AVehicle("VIN-2", drivers: 1), APolicy("VIN-2")));

        readiness.Ready.Should().BeFalse();
        readiness.Reasons.Should().Equal("1 vehicle not ready");
    }

    [Fact]
    public async Task The_query_reads_the_route_the_truck_list_and_each_vehicles_insurance()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(ConvoyTestData.AReadModel());
        repository.GetRouteAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([ConvoyTestData.AStop(1)]);
        repository.ListVehiclesAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([AVehicle()]);
        repository.GetInsuranceAsync(ConvoyTestData.Id, "VIN-1", Arg.Any<CancellationToken>()).Returns(APolicy());

        var readiness = await new GetConvoyReadinessHandler(repository).HandleAsync(
            new GetConvoyReadinessQuery(ConvoyTestData.Id), TestContext.Current.CancellationToken);

        readiness!.Ready.Should().BeTrue();
    }

    [Fact]
    public async Task There_is_no_readiness_for_a_convoy_that_does_not_exist()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns((ConvoyReadModel?)null);

        var readiness = await new GetConvoyReadinessHandler(repository).HandleAsync(
            new GetConvoyReadinessQuery(ConvoyTestData.Id), TestContext.Current.CancellationToken);

        readiness.Should().BeNull();
    }
}

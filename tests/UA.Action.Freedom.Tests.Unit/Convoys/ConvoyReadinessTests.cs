using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// Whether a convoy is ready to travel. Advisory only — nothing is blocked by it. A vehicle is
/// ready with two drivers <em>on each leg</em> and insurance in cover for the departure date; a
/// convoy is ready when it has a route and every vehicle still travelling with it is ready.
/// </summary>
/// <remarks>
/// Crew is per leg because a handover at the European border is a real event, so "two drivers" has
/// to be asked twice — a vehicle fully crewed out of the UK with nobody to take it into Ukraine is
/// not ready, and the old single count could not say so.
/// </remarks>
public class ConvoyReadinessTests
{
    private static readonly DateTime Departs = ConvoyTestData.Start;

    private static ConvoyVehicleReadModel AVehicle(
        string vin = "VIN-1",
        int ukDrivers = 2,
        int borderDrivers = 2,
        int passengers = 0,
        DateTime? withdrawnAt = null) =>
        new(vin, "PL-" + vin[^1], 1_800, ukDrivers, passengers, borderDrivers, passengers, withdrawnAt,
            withdrawnAt is null ? null : "Gearbox failure");

    private static VehicleInsuranceReadModel APolicy(
        string vin = "VIN-1", int startOffset = -7, int endOffset = 30, DateTime? voidedAt = null) => new(
        ConvoyTestData.Id, vin, "Ukraine Aid Mutual", "POL-1",
        Departs.Date.AddDays(startOffset), Departs.Date.AddDays(endOffset), null, "sub", Departs.AddDays(-10), voidedAt);

    private static ConvoyReadinessReadModel Assess(
        bool routePlanned, params (ConvoyVehicleReadModel Vehicle, VehicleInsuranceReadModel? Policy)[] vehicles) =>
        ConvoyReadiness.Assess(Departs, routePlanned, vehicles);

    [Fact]
    public void A_convoy_with_a_route_and_every_vehicle_crewed_on_both_legs_and_insured_is_ready()
    {
        var readiness = Assess(routePlanned: true, (AVehicle(), APolicy()));

        readiness.Ready.Should().BeTrue();
        readiness.Reasons.Should().BeEmpty();
        readiness.Vehicles.Single().Ready.Should().BeTrue();
    }

    [Fact]
    public void Passengers_do_not_count_towards_the_two_drivers()
    {
        var readiness = Assess(routePlanned: true, (AVehicle(ukDrivers: 1, passengers: 3), APolicy()));

        var vehicle = readiness.Vehicles.Single();
        vehicle.Ready.Should().BeFalse();
        vehicle.Reasons.Should().Equal("Fewer than two drivers on the UK to Europe leg");
        readiness.Ready.Should().BeFalse();
    }

    [Fact]
    public void A_vehicle_crewed_out_of_the_UK_but_not_into_Ukraine_is_not_ready()
    {
        // The case the old single crew count could not express: fully crewed to the border and
        // nobody booked to take it on.
        var vehicle = Assess(routePlanned: true, (AVehicle(borderDrivers: 0), APolicy())).Vehicles.Single();

        vehicle.Ready.Should().BeFalse();
        vehicle.Reasons.Should().Equal("Fewer than two drivers on the Europe to Ukraine leg");
    }

    [Fact]
    public void Each_leg_reports_its_own_driver_count()
    {
        var vehicle = Assess(routePlanned: true, (AVehicle(ukDrivers: 2, borderDrivers: 1), APolicy())).Vehicles.Single();

        vehicle.Legs.Should().SatisfyRespectively(
            uk =>
            {
                uk.Leg.Should().Be(JourneyLeg.Uk);
                uk.Drivers.Should().Be(2);
                uk.Ready.Should().BeTrue();
            },
            border =>
            {
                border.Leg.Should().Be(JourneyLeg.Border);
                border.Drivers.Should().Be(1);
                border.Ready.Should().BeFalse();
            });
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
        var vehicle = Assess(routePlanned: true, (AVehicle(ukDrivers: 0, borderDrivers: 0), null)).Vehicles.Single();

        vehicle.Reasons.Should().Equal(
            "Fewer than two drivers on the UK to Europe leg",
            "Fewer than two drivers on the Europe to Ukraine leg",
            "Insurance not recorded");
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

        readiness.Reasons.Should().Equal("No vehicles on the truck list");
    }

    [Fact]
    public void One_unready_vehicle_makes_the_convoy_not_ready()
    {
        var readiness = Assess(
            routePlanned: true,
            (AVehicle("VIN-1"), APolicy("VIN-1")),
            (AVehicle("VIN-2", ukDrivers: 1), APolicy("VIN-2")));

        readiness.Ready.Should().BeFalse();
        readiness.Reasons.Should().Equal("1 vehicle not ready");
    }

    [Fact]
    public void A_withdrawn_vehicle_is_not_judged_and_does_not_hold_the_convoy_back()
    {
        // It broke down and left. It has no crew to find and no insurance to renew — holding the
        // rest of the convoy for it would be reporting a problem nobody can fix.
        var readiness = Assess(
            routePlanned: true,
            (AVehicle("VIN-1"), APolicy("VIN-1")),
            (AVehicle("VIN-2", ukDrivers: 0, borderDrivers: 0, withdrawnAt: Departs.AddDays(2)), null));

        readiness.Ready.Should().BeTrue();
        readiness.Reasons.Should().BeEmpty();
        readiness.Vehicles.Should().ContainSingle().Which.Vin.Should().Be("VIN-1");
    }

    [Fact]
    public void A_convoy_whose_only_vehicle_has_been_withdrawn_has_nothing_travelling()
    {
        var readiness = Assess(
            routePlanned: true,
            (AVehicle("VIN-1", withdrawnAt: Departs.AddDays(2)), APolicy("VIN-1")));

        readiness.Ready.Should().BeFalse();
        readiness.Reasons.Should().Equal("No vehicles on the truck list");
    }

    [Fact]
    public async Task The_query_reads_the_route_the_truck_list_and_each_vehicles_insurance()
    {
        var convoys = Substitute.For<IConvoyRepository>();
        var truckList = Substitute.For<IConvoyVehicleRepository>();
        convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(ConvoyTestData.AReadModel());
        convoys.GetRouteAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([ConvoyTestData.AStop(1)]);
        truckList.ListAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([AVehicle()]);
        truckList.GetInsuranceAsync(ConvoyTestData.Id, "VIN-1", Arg.Any<CancellationToken>()).Returns(APolicy());

        var readiness = await new GetConvoyReadinessHandler(convoys, truckList).HandleAsync(
            new GetConvoyReadinessQuery(ConvoyTestData.Id), TestContext.Current.CancellationToken);

        readiness!.Ready.Should().BeTrue();
    }

    [Fact]
    public async Task There_is_no_readiness_for_a_convoy_that_does_not_exist()
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns((ConvoyReadModel?)null);

        var readiness = await new GetConvoyReadinessHandler(convoys, Substitute.For<IConvoyVehicleRepository>())
            .HandleAsync(new GetConvoyReadinessQuery(ConvoyTestData.Id), TestContext.Current.CancellationToken);

        readiness.Should().BeNull();
    }
}

using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// ADR 0008: readiness is a list of requirements, each with a scope, a state, an owner and a severity. The blocking ones
/// stop a convoy departing and nothing overrides them. One clock (<c>asOf</c>) answers every date rule.
/// </summary>
public class ConvoyReadinessTests
{
    private static readonly DateTime AsOf = ConvoyTestData.Start;
    private static readonly Guid ReceiverA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static DeclarationFact Declared(DeclarationKind kind, DeclarationStatus status = DeclarationStatus.Accepted, Guid? receiver = null) =>
        new(kind, receiver, status);

    private static VehicleInsuranceReadModel APolicy(
        int startOffset = -7, int endOffset = 30, DateTime? voidedAt = null, params Guid[] uncoveredDrivers) => new(
        ConvoyTestData.Id, "VIN-1", "Ukraine Aid Mutual", "POL-1",
        AsOf.Date.AddDays(startOffset), AsOf.Date.AddDays(endOffset), null, Guid.Empty, AsOf.AddDays(-10), voidedAt)
    {
        UncoveredDrivers = uncoveredDrivers,
    };

    private static FerryBookingReadModel AFerry(string? ticket = "E-ticket 7781") =>
        new(ConvoyTestData.Id, "VIN-1", "P&O", "REF-1", AsOf.AddHours(8), ticket, null);

    /// <summary>A vehicle that meets every blocking requirement. A test changes the one thing it is about.</summary>
    private static VehicleReadinessFacts AReadyVehicle(
        string vin = "VIN-1",
        int drivers = 2,
        int passengers = 0,
        DateTime? withdrawnAt = null,
        VehicleInsuranceReadModel? policy = null,
        FerryBookingReadModel? ferry = null,
        bool noFerry = false,
        bool handoverRegistered = true,
        IReadOnlyList<CargoBoxFact>? boxes = null,
        IReadOnlyList<DeclarationFact>? declarations = null,
        VehicleItemFacts? items = null,
        bool noPolicy = false) => new(
        new ConvoyVehicleReadModel(vin, "AB12CDE", 1_800, drivers, passengers, withdrawnAt, withdrawnAt is null ? null : "Gearbox failure"),
        noPolicy ? null : policy ?? APolicy(),
        noFerry ? null : ferry ?? AFerry(),
        handoverRegistered,
        boxes ?? [new CargoBoxFact(1, Validated: true, ReceiverRegistered: true, ReceiverRef: ReceiverA)],
        declarations ??
        [
            Declared(DeclarationKind.Gmr),
            Declared(DeclarationKind.Ens),
            Declared(DeclarationKind.Elo),
            Declared(DeclarationKind.GoodsList, receiver: ReceiverA),
        ],
        items ?? VehicleItemFacts.None);

    private static ConvoyReadinessFacts Facts(
        bool route = true,
        bool leader = true,
        AccommodationFacts? accommodation = null,
        IReadOnlyList<string>? budgetAdvice = null,
        params VehicleReadinessFacts[] vehicles) => new(
        ConvoyTestData.AReadModel(),
        route,
        leader,
        accommodation ?? AccommodationFacts.Covered,
        vehicles.Length == 0 ? [AReadyVehicle()] : vehicles,
        budgetAdvice ?? []);

    private static ConvoyReadinessReadModel Assess(ConvoyReadinessFacts facts) => ConvoyReadiness.Assess(facts, AsOf);

    private static Requirement Requirement(ConvoyReadinessReadModel readiness, string code, string? vin = null) =>
        readiness.Requirements.Single(requirement => requirement.Code == code && requirement.Vin == vin);

    private static void BlocksOn(ConvoyReadinessReadModel readiness, string code, string? vin = null)
    {
        var requirement = Requirement(readiness, code, vin);

        requirement.State.Should().NotBe(RequirementState.Done);
        requirement.Severity.Should().Be(RequirementSeverity.Blocking);
        readiness.Ready.Should().BeFalse();
    }

    private static void Passes(ConvoyReadinessReadModel readiness, string code, string? vin = null)
    {
        Requirement(readiness, code, vin).State.Should().Be(RequirementState.Done);
    }

    [Fact]
    public void A_convoy_that_meets_every_requirement_is_ready()
    {
        var readiness = Assess(Facts());

        readiness.Ready.Should().BeTrue();
        readiness.Reasons.Should().BeEmpty();
        readiness.Requirements.Where(requirement => requirement.Severity == RequirementSeverity.Blocking)
            .Should().OnlyContain(requirement => requirement.State == RequirementState.Done);
    }

    [Fact]
    public void Every_requirement_names_its_scope_owner_and_where_to_fix_it()
    {
        var readiness = Assess(Facts(leader: false));

        var leader = Requirement(readiness, "leader");

        leader.Scope.Should().Be(RequirementScope.Convoy);
        leader.OwnerRole.Should().Be("Dispatcher");
        leader.ResolveLink.Should().Be($"/convoys/{ConvoyTestData.Id}/leader");
        Requirement(readiness, "driver", "VIN-1").Scope.Should().Be(RequirementScope.Vehicle);
    }

    [Fact]
    public void A_convoy_with_no_route_is_not_ready()
    {
        var readiness = Assess(Facts(route: false));

        BlocksOn(readiness, "route");
        readiness.RoutePlanned.Should().BeFalse();
        readiness.Reasons.Should().Equal("No route planned");
    }

    [Fact]
    public void A_convoy_needs_a_leader()
    {
        BlocksOn(Assess(Facts(leader: false)), "leader");
        Passes(Assess(Facts(leader: true)), "leader");
    }

    [Fact]
    public void Every_crew_member_needs_a_bed_or_to_arrange_their_own_at_each_overnight_stop()
    {
        BlocksOn(Assess(Facts(accommodation: new AccommodationFacts(AllCovered: false, MissingCount: 3, LeftoverBookings: 0))), "accommodation");
        Passes(Assess(Facts(accommodation: AccommodationFacts.Covered)), "accommodation");
    }

    [Fact]
    public void A_convoy_with_no_vehicles_travelling_is_not_ready()
    {
        var readiness = Assess(Facts(vehicles: AReadyVehicle(withdrawnAt: AsOf.AddDays(2))));

        BlocksOn(readiness, "vehicles");
        readiness.Reasons.Should().Equal("No vehicles on the truck list");
    }

    [Fact]
    public void A_vehicle_needs_at_least_one_driver_and_passengers_do_not_count()
    {
        BlocksOn(Assess(Facts(vehicles: AReadyVehicle(drivers: 0, passengers: 3))), "driver", "VIN-1");
        Passes(Assess(Facts(vehicles: AReadyVehicle(drivers: 1))), "driver", "VIN-1");
    }

    public static TheoryData<string, VehicleInsuranceReadModel?> UninsuredCases => new()
    {
        { "Insurance not recorded", null },
        { "Insurance voided", APolicy(voidedAt: AsOf.AddDays(-1)) },
        { "Insurance does not cover the departure date", APolicy(startOffset: 1) },
        { "Insurance does not cover the departure date", APolicy(endOffset: -1) },
        { "Insurance does not cover every driver", APolicy(uncoveredDrivers: Guid.NewGuid()) },
    };

    [Theory]
    [MemberData(nameof(UninsuredCases))]
    public void A_vehicle_needs_insurance_in_cover_on_the_day_that_names_every_driver(
        string reason, VehicleInsuranceReadModel? policy)
    {
        var readiness = Assess(Facts(vehicles: AReadyVehicle(policy: policy, noPolicy: policy is null)));

        BlocksOn(readiness, "insurance", "VIN-1");
        readiness.Vehicles.Single().Insured.Should().BeFalse();
        readiness.Vehicles.Single().Reasons.Should().Equal(reason);
    }

    [Fact]
    public void Insurance_is_judged_on_the_one_clock_the_caller_supplies()
    {
        var facts = Facts(vehicles: AReadyVehicle(policy: APolicy(endOffset: 2)));

        Passes(ConvoyReadiness.Assess(facts, AsOf), "insurance", "VIN-1");
        BlocksOn(ConvoyReadiness.Assess(facts, AsOf.AddDays(5)), "insurance", "VIN-1");
    }

    [Fact]
    public void A_vehicle_needs_a_ferry_booking()
    {
        BlocksOn(Assess(Facts(vehicles: AReadyVehicle(noFerry: true))), "ferry", "VIN-1");
        Passes(Assess(Facts(vehicles: AReadyVehicle())), "ferry", "VIN-1");
    }

    [Fact]
    public void A_vehicle_needs_a_registered_handover_receiver()
    {
        BlocksOn(Assess(Facts(vehicles: AReadyVehicle(handoverRegistered: false))), "handover-receiver", "VIN-1");
        Passes(Assess(Facts(vehicles: AReadyVehicle(handoverRegistered: true))), "handover-receiver", "VIN-1");
    }

    [Fact]
    public void Every_allocated_box_must_be_validated_and_name_a_registered_receiver()
    {
        var unvalidated = new CargoBoxFact(1, Validated: false, ReceiverRegistered: true, ReceiverRef: ReceiverA);
        var unregistered = new CargoBoxFact(2, Validated: true, ReceiverRegistered: false, ReceiverRef: ReceiverA);
        var noReceiver = new CargoBoxFact(3, Validated: true, ReceiverRegistered: false, ReceiverRef: null);

        BlocksOn(Assess(Facts(vehicles: AReadyVehicle(boxes: [unvalidated]))), "boxes", "VIN-1");
        BlocksOn(Assess(Facts(vehicles: AReadyVehicle(boxes: [unregistered]))), "boxes", "VIN-1");
        BlocksOn(Assess(Facts(vehicles: AReadyVehicle(boxes: [noReceiver]))), "boxes", "VIN-1");
        Passes(Assess(Facts(vehicles: AReadyVehicle(boxes: []))), "boxes", "VIN-1");
    }

    [Theory]
    [InlineData(DeclarationKind.Gmr, "declaration.gmr")]
    [InlineData(DeclarationKind.Ens, "declaration.ens")]
    [InlineData(DeclarationKind.Elo, "declaration.elo")]
    public void Each_instrument_must_be_declared_and_current(DeclarationKind kind, string code)
    {
        var without = Everything().Where(declaration => declaration.Kind != kind).ToList();

        BlocksOn(Assess(Facts(vehicles: AReadyVehicle(declarations: without))), code, "VIN-1");
        Passes(Assess(Facts(vehicles: AReadyVehicle(declarations: Everything()))), code, "VIN-1");
    }

    [Theory]
    [InlineData(DeclarationStatus.Draft)]
    [InlineData(DeclarationStatus.ReadyToFile)]
    [InlineData(DeclarationStatus.Refused)]
    [InlineData(DeclarationStatus.Stale)]
    [InlineData(DeclarationStatus.Withdrawn)]
    public void A_declaration_that_is_not_filed_or_accepted_is_not_current(DeclarationStatus status)
    {
        var declarations = Everything().Select(d => d.Kind == DeclarationKind.Gmr ? d with { Status = status } : d).ToList();

        BlocksOn(Assess(Facts(vehicles: AReadyVehicle(declarations: declarations))), "declaration.gmr", "VIN-1");
    }

    [Theory]
    [InlineData(DeclarationStatus.Filed)]
    [InlineData(DeclarationStatus.Accepted)]
    [InlineData(DeclarationStatus.Closed)]
    public void A_filed_accepted_or_closed_declaration_is_current(DeclarationStatus status)
    {
        var declarations = Everything().Select(d => d with { Status = status }).ToList();

        Passes(Assess(Facts(vehicles: AReadyVehicle(declarations: declarations))), "declaration.gmr", "VIN-1");
    }

    [Fact]
    public void An_elo_waits_for_an_accepted_ens_and_says_so()
    {
        var declarations = Everything().Where(d => d.Kind is not (DeclarationKind.Elo or DeclarationKind.Ens)).ToList();

        var readiness = Assess(Facts(vehicles: AReadyVehicle(declarations: declarations)));

        Requirement(readiness, "declaration.ens", "VIN-1").State.Should().Be(RequirementState.ToDo);
        Requirement(readiness, "declaration.elo", "VIN-1").State.Should().Be(RequirementState.Blocked);
    }

    [Fact]
    public void A_goods_list_is_needed_for_every_receiver_the_load_names()
    {
        var receiverB = Guid.NewGuid();
        var boxes = new[]
        {
            new CargoBoxFact(1, true, true, ReceiverA),
            new CargoBoxFact(2, true, true, receiverB),
        };

        BlocksOn(Assess(Facts(vehicles: AReadyVehicle(boxes: boxes))), "declaration.goods-list", "VIN-1");

        var both = Everything().Append(Declared(DeclarationKind.GoodsList, receiver: receiverB)).ToList();
        Passes(Assess(Facts(vehicles: AReadyVehicle(boxes: boxes, declarations: both))), "declaration.goods-list", "VIN-1");
    }

    [Fact]
    public void An_empty_vehicle_needs_no_goods_list()
    {
        var declarations = Everything().Where(d => d.Kind != DeclarationKind.GoodsList).ToList();

        Passes(Assess(Facts(vehicles: AReadyVehicle(boxes: [], declarations: declarations))), "declaration.goods-list", "VIN-1");
    }

    [Fact]
    public void A_withdrawn_vehicle_is_not_judged_and_does_not_hold_the_convoy_back()
    {
        var readiness = Assess(Facts(vehicles:
        [
            AReadyVehicle("VIN-1"),
            AReadyVehicle("VIN-2", drivers: 0, noPolicy: true, noFerry: true, withdrawnAt: AsOf.AddDays(2)),
        ]));

        readiness.Ready.Should().BeTrue();
        readiness.Vehicles.Should().ContainSingle().Which.Vin.Should().Be("VIN-1");
        readiness.Requirements.Should().NotContain(requirement => requirement.Vin == "VIN-2");
    }

    [Fact]
    public void One_unready_vehicle_makes_the_convoy_not_ready_and_the_count_is_reported()
    {
        var readiness = Assess(Facts(vehicles: [AReadyVehicle("VIN-1"), AReadyVehicle("VIN-2", drivers: 0)]));

        readiness.Ready.Should().BeFalse();
        readiness.Reasons.Should().Equal("1 vehicle not ready");
        readiness.Vehicles.Single(vehicle => vehicle.Vin == "VIN-2").Reasons.Should().Equal("No driver assigned");
    }

    [Fact]
    public void Every_reason_a_vehicle_is_not_ready_is_listed()
    {
        var vehicle = Assess(Facts(vehicles: AReadyVehicle(drivers: 0, noPolicy: true))).Vehicles.Single();

        vehicle.Reasons.Should().Equal("No driver assigned", "Insurance not recorded");
    }

    // ---- Advisories (P17): none of these blocks departure ----

    private static void Warns(ConvoyReadinessReadModel readiness, string code, string? vin = null)
    {
        var requirement = Requirement(readiness, code, vin);

        requirement.State.Should().Be(RequirementState.Warning);
        requirement.Severity.Should().Be(RequirementSeverity.Advisory);
        readiness.Ready.Should().BeTrue("an advisory never blocks");
    }

    [Fact]
    public void One_driver_is_advised_to_have_a_second()
    {
        var readiness = Assess(Facts(vehicles: AReadyVehicle(drivers: 1)));

        Warns(readiness, "advisory.second-driver", "VIN-1");
        readiness.Vehicles.Single().Advisories.Should().Equal("Only one driver; two are advised");
    }

    [Fact]
    public void Two_drivers_raise_no_driver_advice()
    {
        Assess(Facts(vehicles: AReadyVehicle(drivers: 2))).Requirements
            .Should().NotContain(requirement => requirement.Code == "advisory.second-driver");
    }

    [Fact]
    public void A_booking_left_behind_by_a_departed_crew_member_is_advised()
    {
        Warns(Assess(Facts(accommodation: new AccommodationFacts(true, 0, LeftoverBookings: 2))), "advisory.leftover-booking");
    }

    [Fact]
    public void Insurance_that_ends_before_the_journey_does_is_advised()
    {
        var endsEarly = APolicy(endOffset: 1);

        Warns(Assess(Facts(vehicles: AReadyVehicle(policy: endsEarly))), "advisory.insurance-journey", "VIN-1");
    }

    [Fact]
    public void A_ferry_booking_without_ticket_details_is_advised()
    {
        Warns(Assess(Facts(vehicles: AReadyVehicle(ferry: AFerry(ticket: null)))), "advisory.ferry-ticket", "VIN-1");
    }

    [Fact]
    public void Items_without_a_value_or_a_donor_are_advised()
    {
        var readiness = Assess(Facts(vehicles: AReadyVehicle(items: new VehicleItemFacts(WithoutValue: 2, WithoutDonor: 1, Expired: 0, ShortShelfLife: 0))));

        Warns(readiness, "advisory.item-incomplete", "VIN-1");
    }

    [Fact]
    public void Expired_or_short_dated_items_are_advised()
    {
        var readiness = Assess(Facts(vehicles: AReadyVehicle(items: new VehicleItemFacts(0, 0, Expired: 1, ShortShelfLife: 2))));

        Warns(readiness, "advisory.shelf-life", "VIN-1");
    }

    [Fact]
    public void Budget_advice_is_carried_on_the_convoy_and_never_blocks()
    {
        var readiness = Assess(Facts(budgetAdvice: ["No budget set"]));

        Warns(readiness, "advisory.budget");
        readiness.Advisories.Should().Equal("No budget set");
    }

    private static List<DeclarationFact> Everything() =>
    [
        Declared(DeclarationKind.Gmr),
        Declared(DeclarationKind.Ens),
        Declared(DeclarationKind.Elo),
        Declared(DeclarationKind.GoodsList, receiver: ReceiverA),
    ];
}

using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Data.Convoys;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.Convoys.ConvoyFixtures;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Convoys;

/// <summary>
/// The Dapper <see cref="ConvoyVehicleRepository"/> against a real database: the truck list, the
/// crew of each vehicle on it, and the insurance that names its drivers.
/// </summary>
/// <remarks>
/// These are the constraints that make the consolidation hold, and none of them can be tested
/// without a database: the composite key that ties a manifest to a truck-list entry, the unique
/// index that gives a person one seat per convoy, the conditional insert that keeps a vehicle off
/// two convoys at once, and the transaction that takes a driver off the insurance in the same
/// breath as off the crew.
/// </remarks>
[Trait("Category", "Integration")]
public class ConvoyVehicleRepositoryTests
{
    private static async Task<(ConvoyRepository Convoys, ConvoyVehicleRepository TruckList)> ConnectOrSkipAsync(
        CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync(Probe, cancellationToken);
        await EnsureRecorderAsync();
        return (new ConvoyRepository(ConnectionFactory(), Unattributed), new ConvoyVehicleRepository(ConnectionFactory(), Unattributed));
    }

    [Fact]
    public async Task Adds_a_vehicle_to_the_truck_list_and_takes_it_off_again()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();

        try
        {
            await AddVehicleAsync(vin);

            (await truckList.AddAsync(id, vin, cancellationToken)).Should().Be(AddToTruckListResult.Added);

            // Adding it again is the state the caller asked for, not an error.
            (await truckList.AddAsync(id, vin, cancellationToken)).Should().Be(AddToTruckListResult.AlreadyOnThisConvoy);

            (await truckList.ListAsync(id, cancellationToken)).Should().ContainSingle(vehicle => vehicle.Vin == vin);
            (await truckList.GetAsync(id, vin, cancellationToken))!.Travelling.Should().BeTrue();

            (await truckList.RemoveAsync(id, vin, cancellationToken)).Should().BeTrue();
            (await truckList.ListAsync(id, cancellationToken)).Should().BeEmpty();
            (await truckList.GetAsync(id, vin, cancellationToken)).Should().BeNull();

            // Removing something that is not on this convoy is a caller mistake, not a no-op.
            (await truckList.RemoveAsync(id, vin, cancellationToken)).Should().BeFalse();

            (await truckList.AddAsync(id, "NOSUCHVIN000000", cancellationToken))
                .Should().Be(AddToTruckListResult.VehicleNotFound);
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task Withdrawing_a_vehicle_keeps_its_row_its_crew_and_its_insurance()
    {
        // The breakdown case. The manifest goes on describing a load that is real, so nothing here
        // may be deleted — and the vehicle is free to join a later convoy.
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var later = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var driver = await AddDriverAsync("Olena", "Bondar");
        var withdrawnAt = new DateTime(2026, 9, 3, 14, 30, 0, DateTimeKind.Utc);

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(id, vin, cancellationToken);
            await truckList.AssignCrewAsync(id, vin, driver, CrewRole.Driver, cancellationToken);
            await truckList.RecordInsuranceAsync(AnInsurance(id, vin), cancellationToken);
            await AddManifestAsync(id, vin, ManifestStatus.InTransit);

            (await truckList.WithdrawAsync(id, vin, "Gearbox failure near Poznan", withdrawnAt, cancellationToken))
                .Should().BeTrue();

            var entry = await truckList.GetAsync(id, vin, cancellationToken);
            entry!.Withdrawn.Should().BeTrue();
            entry.WithdrawnReason.Should().Be("Gearbox failure near Poznan");
            (await WithdrawnAtAsync(id, vin)).Should().Be(withdrawnAt);

            // The record of who went and under what cover survives, and so does the manifest.
            (await truckList.ListCrewAsync(id, vin, cancellationToken)).Should().ContainSingle();
            (await truckList.GetInsuranceAsync(id, vin, cancellationToken)).Should().NotBeNull();
            (await ScalarAsync(
                "SELECT COUNT(1) FROM dbo.Manifest WHERE ConvoyId = @id AND Vin = @vin", ("@id", id), ("@vin", vin)))
                .Should().Be(1);

            // Withdrawing twice is settled by the conditional UPDATE, not by a read-then-write.
            (await truckList.WithdrawAsync(id, vin, "again", DateTime.UtcNow, cancellationToken)).Should().BeFalse();

            // And it may now join the next convoy.
            (await truckList.AddAsync(later, vin, cancellationToken)).Should().Be(AddToTruckListResult.Added);
        }
        finally
        {
            await RemoveManifestsAsync(id);
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
            await RemoveConvoyAsync(later);
            await RemovePeopleAsync(driver);
        }
    }

    [Fact]
    public async Task A_withdrawn_vehicle_takes_no_more_crew_or_insurance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var driver = await AddDriverAsync("Olena", "Bondar");

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(id, vin, cancellationToken);
            await truckList.WithdrawAsync(id, vin, "Accident", DateTime.UtcNow, cancellationToken);

            (await truckList.AssignCrewAsync(id, vin, driver, CrewRole.Driver, cancellationToken))
                .Should().Be(AssignCrewResult.VehicleNotOnConvoy);
            (await truckList.RecordInsuranceAsync(AnInsurance(id, vin), cancellationToken)).Should().BeFalse();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
            await RemovePeopleAsync(driver);
        }
    }

    [Theory]
    [InlineData(InspectionStatus.Pending)]
    [InlineData(InspectionStatus.Inspecting)]
    [InlineData(InspectionStatus.Failed)]
    public async Task Refuses_a_vehicle_that_has_not_passed_its_inspection(InspectionStatus inspection)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();

        try
        {
            await AddVehicleAsync(vin, inspection);

            (await truckList.AddAsync(id, vin, cancellationToken)).Should().Be(AddToTruckListResult.NotPassedInspection);
            (await ConvoyOfAsync(vin)).Should().BeNull();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task Will_not_take_a_vehicle_off_another_convoy()
    {
        // Otherwise adding to a second convoy would silently empty a seat on the first —
        // including one whose truck list is already published and manifested.
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var first = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var second = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(first, vin, cancellationToken);

            (await truckList.AddAsync(second, vin, cancellationToken)).Should().Be(AddToTruckListResult.OnAnotherConvoy);
            (await ConvoyOfAsync(vin)).Should().Be(first);
            (await TruckListCountAsync(second)).Should().Be(0);
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(first);
            await RemoveConvoyAsync(second);
        }
    }

    [Fact]
    public async Task A_handed_over_vehicle_never_joins_another_convoy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();

        try
        {
            await AddVehicleAsync(vin);
            await ExecuteAsync("UPDATE dbo.Vehicle SET HandedOverAt = SYSUTCDATETIME() WHERE Vin = @vin", ("@vin", vin));

            (await truckList.AddAsync(id, vin, cancellationToken)).Should().Be(AddToTruckListResult.HandedOver);
            (await ConvoyOfAsync(vin)).Should().BeNull();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task Crews_a_vehicle_per_leg_and_lists_the_crew_by_leg_then_surname()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var zelenko = await AddDriverAsync("Taras", "Zelenko");
        var bondar = await AddDriverAsync("Olena", "Bondar");

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(id, vin, cancellationToken);

            (await truckList.AssignCrewAsync(id, vin, zelenko, CrewRole.Driver, cancellationToken))
                .Should().Be(AssignCrewResult.Assigned);
            (await truckList.AssignCrewAsync(id, vin, bondar, CrewRole.Driver, cancellationToken))
                .Should().Be(AssignCrewResult.Assigned);

            // The same seat twice is settled by the unique index.
            (await truckList.AssignCrewAsync(id, vin, bondar, CrewRole.Driver, cancellationToken))
                .Should().Be(AssignCrewResult.AlreadyAssigned);

            var crew = await truckList.ListCrewAsync(id, vin, cancellationToken);
            crew!.Select(member => member.LastName).Should().Equal("Bondar", "Zelenko");

            (await truckList.ListAsync(id, cancellationToken)).Single().DriverCount.Should().Be(2);

            (await truckList.UnassignCrewAsync(id, vin, zelenko, cancellationToken)).Should().BeTrue();
            (await truckList.UnassignCrewAsync(id, vin, zelenko, cancellationToken)).Should().BeFalse();
            (await truckList.ListCrewAsync(id, vin, cancellationToken))!.Should().ContainSingle();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
            await RemovePeopleAsync(zelenko, bondar);
        }
    }

    [Fact]
    public async Task Passengers_ride_with_a_role_and_are_not_counted_as_drivers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var driver = await AddDriverAsync("Olena", "Bondar");
        var passenger = await AddDriverAsync("Mykola", "Shevchuk");

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(id, vin, cancellationToken);

            await truckList.AssignCrewAsync(id, vin, driver, CrewRole.Driver, cancellationToken);
            await truckList.AssignCrewAsync(id, vin, passenger, CrewRole.Passenger, cancellationToken);

            var crew = await truckList.ListCrewAsync(id, vin, cancellationToken);
            crew!.Select(member => (member.LastName, member.Role))
                .Should().Equal(("Bondar", CrewRole.Driver), ("Shevchuk", CrewRole.Passenger));

            var onConvoy = (await truckList.ListAsync(id, cancellationToken)).Single();
            onConvoy.DriverCount.Should().Be(1);
            onConvoy.PassengerCount.Should().Be(1);
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
            await RemovePeopleAsync(driver, passenger);
        }
    }

    [Fact]
    public async Task A_person_takes_one_seat_per_convoy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var next = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var first = NewVin();
        var second = NewVin();
        var third = NewVin();
        var driver = await AddDriverAsync("Olena", "Bondar");

        try
        {
            await AddVehicleAsync(first);
            await AddVehicleAsync(second);
            await AddVehicleAsync(third);
            await truckList.AddAsync(id, first, cancellationToken);
            await truckList.AddAsync(id, second, cancellationToken);
            await truckList.AddAsync(next, third, cancellationToken);

            (await truckList.AssignCrewAsync(id, first, driver, CrewRole.Driver, cancellationToken))
                .Should().Be(AssignCrewResult.Assigned);

            // Two vehicles on the same convoy: refused.
            (await truckList.AssignCrewAsync(id, second, driver, CrewRole.Passenger, cancellationToken))
                .Should().Be(AssignCrewResult.OnAnotherVehicle);
            (await truckList.ListCrewAsync(id, second, cancellationToken)).Should().BeEmpty();

            // A different convoy is a different journey entirely.
            (await truckList.AssignCrewAsync(next, third, driver, CrewRole.Driver, cancellationToken))
                .Should().Be(AssignCrewResult.Assigned);
        }
        finally
        {
            await RemoveVehicleAsync(first);
            await RemoveVehicleAsync(second);
            await RemoveVehicleAsync(third);
            await RemoveConvoyAsync(id);
            await RemoveConvoyAsync(next);
            await RemovePeopleAsync(driver);
        }
    }

    [Fact]
    public async Task Will_not_crew_or_list_a_vehicle_that_is_not_on_the_convoy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var driver = await AddDriverAsync("Olena", "Bondar");

        try
        {
            await AddVehicleAsync(vin);

            (await truckList.AssignCrewAsync(id, vin, driver, CrewRole.Driver, cancellationToken))
                .Should().Be(AssignCrewResult.VehicleNotOnConvoy);

            // null, not [] — "no such vehicle here" and "nobody crewing it" are different answers.
            (await truckList.ListCrewAsync(id, vin, cancellationToken)).Should().BeNull();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
            await RemovePeopleAsync(driver);
        }
    }

    [Fact]
    public async Task Unassigning_a_crew_member_names_the_convoy_as_well_as_the_vehicle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var other = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var driver = await AddDriverAsync("Olena", "Bondar");

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(id, vin, cancellationToken);
            await truckList.AssignCrewAsync(id, vin, driver, CrewRole.Driver, cancellationToken);

            (await truckList.UnassignCrewAsync(other, vin, driver, cancellationToken)).Should().BeFalse();
            (await truckList.ListCrewAsync(id, vin, cancellationToken))!.Should().ContainSingle();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
            await RemoveConvoyAsync(other);
            await RemovePeopleAsync(driver);
        }
    }

    [Fact]
    public async Task Taking_a_vehicle_off_a_convoy_stands_its_crew_down()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var driver = await AddDriverAsync("Olena", "Bondar");

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(id, vin, cancellationToken);
            await truckList.AssignCrewAsync(id, vin, driver, CrewRole.Driver, cancellationToken);

            // The crew cascades from the truck-list row rather than being cleared by hand.
            await truckList.RemoveAsync(id, vin, cancellationToken);
            await truckList.AddAsync(id, vin, cancellationToken);

            (await truckList.ListCrewAsync(id, vin, cancellationToken)).Should().BeEmpty();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
            await RemovePeopleAsync(driver);
        }
    }

    [Fact]
    public async Task Records_insurance_for_a_vehicle_on_the_convoy_and_reads_it_back()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var elsewhere = NewVin();

        try
        {
            await AddVehicleAsync(vin);
            await AddVehicleAsync(elsewhere);
            await truckList.AddAsync(id, vin, cancellationToken);

            (await truckList.RecordInsuranceAsync(AnInsurance(id, vin), cancellationToken)).Should().BeTrue();

            // Not on this convoy, so there is nothing to insure for it.
            (await truckList.RecordInsuranceAsync(AnInsurance(id, elsewhere), cancellationToken)).Should().BeFalse();

            var stored = await truckList.GetInsuranceAsync(id, vin, cancellationToken);
            stored!.PolicyNumber.Should().Be("POL-1");
            stored.CoverEnd.Should().Be(new DateTime(2026, 9, 30));
            stored.CostGbp.Should().Be(412.50m);
            stored.RecordedBy.Should().Be(RecorderId);
            stored.VoidedAt.Should().BeNull();
            (await truckList.GetInsuranceAsync(id, elsewhere, cancellationToken)).Should().BeNull();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveVehicleAsync(elsewhere);
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task Adding_a_driver_leaves_them_uncovered_and_recording_again_covers_everyone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var first = await AddDriverAsync("Olena", "Bondar");
        var second = await AddDriverAsync("Taras", "Zelenko");

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(id, vin, cancellationToken);
            await truckList.AssignCrewAsync(id, vin, first, CrewRole.Driver, cancellationToken);
            await truckList.RecordInsuranceAsync(AnInsurance(id, vin), cancellationToken);
            (await truckList.GetInsuranceAsync(id, vin, cancellationToken))!.UncoveredDrivers.Should().BeEmpty();

            await truckList.AssignCrewAsync(id, vin, second, CrewRole.Driver, cancellationToken);
            var afterAdding = await truckList.GetInsuranceAsync(id, vin, cancellationToken);
            afterAdding!.VoidedAt.Should().BeNull();
            afterAdding.UncoveredDrivers.Should().Equal(second);

            await truckList.RecordInsuranceAsync(AnInsurance(id, vin, "POL-2"), cancellationToken);
            var renewed = await truckList.GetInsuranceAsync(id, vin, cancellationToken);
            renewed!.PolicyNumber.Should().Be("POL-2");
            renewed.UncoveredDrivers.Should().BeEmpty();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
            await RemovePeopleAsync(first, second);
        }
    }

    [Fact]
    public async Task Removing_a_driver_keeps_the_policy_in_cover_for_the_others()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var leaving = await AddDriverAsync("Olena", "Bondar");
        var staying = await AddDriverAsync("Taras", "Zelenko");

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(id, vin, cancellationToken);
            await truckList.AssignCrewAsync(id, vin, leaving, CrewRole.Driver, cancellationToken);
            await truckList.AssignCrewAsync(id, vin, staying, CrewRole.Driver, cancellationToken);
            await truckList.RecordInsuranceAsync(AnInsurance(id, vin), cancellationToken);

            await truckList.UnassignCrewAsync(id, vin, leaving, cancellationToken);

            var policy = await truckList.GetInsuranceAsync(id, vin, cancellationToken);
            policy!.VoidedAt.Should().BeNull();
            policy.UncoveredDrivers.Should().BeEmpty();
            (await ScalarAsync(
                "SELECT COUNT(1) FROM dbo.ConvoyVehicleInsuranceDriver WHERE Vin = @vin", ("@vin", vin))).Should().Be(1);

            // They come back: not named on the policy any more, so uncovered until it is recorded.
            await truckList.AssignCrewAsync(id, vin, leaving, CrewRole.Driver, cancellationToken);
            (await truckList.GetInsuranceAsync(id, vin, cancellationToken))!.UncoveredDrivers.Should().Equal(leaving);
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
            await RemovePeopleAsync(leaving, staying);
        }
    }

    [Fact]
    public async Task Insurance_goes_with_the_vehicle_off_the_convoy_and_with_a_cancelled_convoy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var cancelled = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var other = NewVin();

        try
        {
            await AddVehicleAsync(vin);
            await AddVehicleAsync(other);
            await truckList.AddAsync(id, vin, cancellationToken);
            await truckList.AddAsync(cancelled, other, cancellationToken);
            await truckList.RecordInsuranceAsync(AnInsurance(id, vin), cancellationToken);
            await truckList.RecordInsuranceAsync(AnInsurance(cancelled, other), cancellationToken);

            await truckList.RemoveAsync(id, vin, cancellationToken);
            await convoys.DeleteAsync(cancelled, cancellationToken);

            (await truckList.GetInsuranceAsync(id, vin, cancellationToken)).Should().BeNull();
            (await ScalarAsync(
                "SELECT COUNT(1) FROM dbo.ConvoyVehicleInsurance WHERE Vin = @vin", ("@vin", other))).Should().Be(0);
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveVehicleAsync(other);
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task A_manifest_cannot_name_a_vehicle_that_is_not_on_the_convoy()
    {
        // The composite foreign key. This is the invariant the whole consolidation exists for: the
        // pair used to be two independent nullable columns and nothing checked they agreed.
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var onConvoy = NewVin();
        var elsewhere = NewVin();

        try
        {
            await AddVehicleAsync(onConvoy);
            await AddVehicleAsync(elsewhere);
            await truckList.AddAsync(id, onConvoy, cancellationToken);

            await AddManifestAsync(id, onConvoy, ManifestStatus.Created);

            var naming = async () => await AddManifestAsync(id, elsewhere, ManifestStatus.Created);
            await naming.Should().ThrowAsync<Exception>();
        }
        finally
        {
            await RemoveManifestsAsync(id);
            await RemoveVehicleAsync(onConvoy);
            await RemoveVehicleAsync(elsewhere);
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task One_vehicle_on_one_convoy_carries_one_manifest()
    {
        // UQ_Manifest_ConvoyVehicle. Arrival asks each vehicle for its finished manifest and has
        // to get one answer; two would be satisfied by whichever finished first.
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(id, vin, cancellationToken);
            await AddManifestAsync(id, vin, ManifestStatus.Created);

            var second = async () => await AddManifestAsync(id, vin, ManifestStatus.Created);
            await second.Should().ThrowAsync<Exception>();
        }
        finally
        {
            await RemoveManifestsAsync(id);
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
        }
    }
}

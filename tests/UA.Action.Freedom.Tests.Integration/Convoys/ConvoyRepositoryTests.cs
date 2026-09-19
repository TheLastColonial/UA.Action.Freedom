using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Data.Convoys;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Convoys;

/// <summary>
/// The Dapper <see cref="ConvoyRepository"/> against real <c>dbo.Convoy</c>,
/// <c>dbo.ConvoyRouteStop</c> and <c>dbo.Vehicle</c>. Needs the local stack up
/// (<c>iac/local</c> + <c>tofu apply</c>) or a <c>ConnectionStrings__Freedom</c> pointing at an
/// equivalent database; skips itself otherwise.
/// </summary>
/// <remarks>
/// Two things here can only be tested against a real database: the route transaction, and the
/// foreign key that releases a convoy's vehicles instead of deleting them.
/// </remarks>
[Trait("Category", "Integration")]
public class ConvoyRepositoryTests
{
    private static readonly DateTime Start = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ExpectedEnd = new(2026, 9, 5, 18, 0, 0, DateTimeKind.Utc);

    private static async Task<ConvoyRepository> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync("SELECT COUNT(1) FROM dbo.Convoy; SELECT COUNT(1) FROM dbo.ConvoyRouteStop;", cancellationToken);
        return new ConvoyRepository(ConnectionFactory());
    }

    private static RouteStopReadModel AStop(int sequence, string city, string postcode) =>
        new(sequence, "Unit 4", "Cross Road", city, "United Kingdom", postcode);

    private static string NewVin() => "IT" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();

    private static Task AddVehicleAsync(string vin, InspectionStatus inspection = InspectionStatus.Passed) => ExecuteAsync(
        "INSERT INTO dbo.Vehicle (Vin, Plate, [Year], WeightKg, InspectionStatus) VALUES (@vin, 'IT12ABC', 2015, 1800, @inspection)",
        ("@vin", vin),
        ("@inspection", (int)inspection));

    private static Task<object?> ConvoyOfAsync(string vin) =>
        ValueAsync("SELECT ConvoyId FROM dbo.Vehicle WHERE Vin = @vin", ("@vin", vin));

    private static Task RemoveVehicleAsync(string vin) =>
        ExecuteAsync("DELETE FROM dbo.Vehicle WHERE Vin = @vin", ("@vin", vin));

    private static Task RemoveConvoyAsync(int id) =>
        ExecuteAsync("DELETE FROM dbo.Convoy WHERE Id = @id", ("@id", id));

    private static async Task<Guid> AddDriverAsync(string firstName, string lastName)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT INTO dbo.Person (Id, FirstName, LastName, DateOfBirth, Joined, IsDriver)
            VALUES (@id, @firstName, @lastName, '1985-01-01', '2024-01-01', 1)
            """,
            ("@id", id),
            ("@firstName", firstName),
            ("@lastName", lastName));
        return id;
    }

    private static Task RemovePeopleAsync(params Guid[] ids) => Task.WhenAll(ids.Select(id =>
        ExecuteAsync("DELETE FROM dbo.VehicleDriver WHERE PersonId = @id; DELETE FROM dbo.Person WHERE Id = @id", ("@id", id))));

    [Fact]
    public async Task Crews_a_vehicle_and_lists_its_drivers_by_surname()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var zelenko = await AddDriverAsync("Taras", "Zelenko");
        var bondar = await AddDriverAsync("Olena", "Bondar");

        try
        {
            await AddVehicleAsync(vin);
            await repository.AssignVehicleAsync(id, vin, cancellationToken);

            (await repository.AssignDriverAsync(id, vin, zelenko, cancellationToken)).Should().Be(AssignDriverResult.Assigned);
            (await repository.AssignDriverAsync(id, vin, bondar, cancellationToken)).Should().Be(AssignDriverResult.Assigned);
            (await repository.AssignDriverAsync(id, vin, bondar, cancellationToken)).Should().Be(AssignDriverResult.AlreadyAssigned);

            var crew = await repository.ListVehicleDriversAsync(id, vin, cancellationToken);
            crew!.Select(driver => driver.LastName).Should().Equal("Bondar", "Zelenko");
            (await repository.ListVehiclesAsync(id, cancellationToken)).Should().ContainSingle()
                .Which.DriverCount.Should().Be(2);

            (await repository.UnassignDriverAsync(id, vin, zelenko, cancellationToken)).Should().BeTrue();
            (await repository.UnassignDriverAsync(id, vin, zelenko, cancellationToken)).Should().BeFalse();
            (await repository.ListVehicleDriversAsync(id, vin, cancellationToken))!.Should().ContainSingle();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
            await RemovePeopleAsync(zelenko, bondar);
        }
    }

    [Fact]
    public async Task Will_not_crew_or_list_a_vehicle_that_is_not_on_the_convoy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var driver = await AddDriverAsync("Olena", "Bondar");

        try
        {
            await AddVehicleAsync(vin);

            (await repository.AssignDriverAsync(id, vin, driver, cancellationToken)).Should().Be(AssignDriverResult.VehicleNotOnConvoy);
            (await repository.ListVehicleDriversAsync(id, vin, cancellationToken)).Should().BeNull();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
            await RemovePeopleAsync(driver);
        }
    }

    [Fact]
    public async Task Unassigning_a_driver_names_the_convoy_as_well_as_the_vehicle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);
        var other = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var driver = await AddDriverAsync("Olena", "Bondar");

        try
        {
            await AddVehicleAsync(vin);
            await repository.AssignVehicleAsync(id, vin, cancellationToken);
            await repository.AssignDriverAsync(id, vin, driver, cancellationToken);

            (await repository.UnassignDriverAsync(other, vin, driver, cancellationToken)).Should().BeFalse();
            (await repository.ListVehicleDriversAsync(id, vin, cancellationToken))!.Should().ContainSingle();
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
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var driver = await AddDriverAsync("Olena", "Bondar");

        try
        {
            await AddVehicleAsync(vin);
            await repository.AssignVehicleAsync(id, vin, cancellationToken);
            await repository.AssignDriverAsync(id, vin, driver, cancellationToken);

            await repository.UnassignVehicleAsync(id, vin, cancellationToken);
            await repository.AssignVehicleAsync(id, vin, cancellationToken);

            (await repository.ListVehicleDriversAsync(id, vin, cancellationToken)).Should().BeEmpty();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
            await RemovePeopleAsync(driver);
        }
    }

    [Fact]
    public async Task Cancelling_a_convoy_stands_its_crews_down()
    {
        // Otherwise the released vehicle carries the old crew to whichever convoy it joins next.
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var cancelled = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);
        var next = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var driver = await AddDriverAsync("Olena", "Bondar");

        try
        {
            await AddVehicleAsync(vin);
            await repository.AssignVehicleAsync(cancelled, vin, cancellationToken);
            await repository.AssignDriverAsync(cancelled, vin, driver, cancellationToken);

            await repository.DeleteAsync(cancelled, cancellationToken);
            await repository.AssignVehicleAsync(next, vin, cancellationToken);

            (await repository.ListVehicleDriversAsync(next, vin, cancellationToken)).Should().BeEmpty();
            (await repository.ListVehiclesAsync(next, cancellationToken)).Should().ContainSingle()
                .Which.DriverCount.Should().Be(0);
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(next);
            await RemovePeopleAsync(driver);
        }
    }

    [Fact]
    public async Task Round_trips_a_convoy_and_hands_back_the_identifier_it_assigned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);

        var id = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            id.Should().BeGreaterThan(0);

            var stored = await repository.GetByIdAsync(id, cancellationToken);

            stored.Should().Be(new ConvoyReadModel(id, Start, ExpectedEnd, TruckListPublishedAt: null));
            stored!.TruckListPublished.Should().BeFalse();
        }
        finally
        {
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task Replaces_a_route_whole_and_reads_it_back_in_order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            await repository.ReplaceRouteAsync(
                id, [AStop(1, "Coventry", "CV1 2AB"), AStop(2, "Warszawa", "80-180")], cancellationToken);

            var route = await repository.GetRouteAsync(id, cancellationToken);
            route.Select(stop => stop.City).Should().ContainInOrder("Coventry", "Warszawa");

            // Replacing is a replacement, not an append: the old stops have to be gone.
            await repository.ReplaceRouteAsync(id, [AStop(1, "Dover", "CT16 1JA")], cancellationToken);

            var replaced = await repository.GetRouteAsync(id, cancellationToken);
            replaced.Should().ContainSingle();
            replaced[0].City.Should().Be("Dover");
        }
        finally
        {
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task Deleting_a_convoy_takes_its_route_with_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);

        await repository.ReplaceRouteAsync(id, [AStop(1, "Coventry", "CV1 2AB")], cancellationToken);

        (await repository.DeleteAsync(id, cancellationToken)).Should().BeTrue();

        // The cascade is what stops a cancelled convoy leaving orphan stops behind.
        (await repository.GetRouteAsync(id, cancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task Publishes_a_truck_list_once_and_refuses_the_second_attempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            var published = await repository.PublishTruckListAsync(id, DateTime.UtcNow, cancellationToken);
            published.Should().BeTrue();

            // The UPDATE is conditional on nothing having published yet, so the database — not
            // the application — is what settles a race between two dispatchers.
            var again = await repository.PublishTruckListAsync(id, DateTime.UtcNow, cancellationToken);
            again.Should().BeFalse();

            var stored = await repository.GetByIdAsync(id, cancellationToken);
            stored!.TruckListPublished.Should().BeTrue();
        }
        finally
        {
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task Assigns_and_releases_a_vehicle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();

        try
        {
            await AddVehicleAsync(vin);

            (await repository.AssignVehicleAsync(id, vin, cancellationToken)).Should().Be(AssignVehicleResult.Assigned);

            // Assigning it again to the convoy it is already on changes nothing and is not an error.
            (await repository.AssignVehicleAsync(id, vin, cancellationToken)).Should().Be(AssignVehicleResult.Assigned);

            var onConvoy = await repository.ListVehiclesAsync(id, cancellationToken);
            onConvoy.Should().ContainSingle(vehicle => vehicle.Vin == vin);

            (await repository.UnassignVehicleAsync(id, vin, cancellationToken)).Should().BeTrue();
            (await repository.ListVehiclesAsync(id, cancellationToken)).Should().BeEmpty();

            // Unassigning something that is not on this convoy is a caller mistake, not a no-op.
            (await repository.UnassignVehicleAsync(id, vin, cancellationToken)).Should().BeFalse();

            // And there is no vehicle with this VIN at all.
            (await repository.AssignVehicleAsync(id, "NOSUCHVIN000000", cancellationToken))
                .Should().Be(AssignVehicleResult.VehicleNotFound);
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
        }
    }

    [Theory]
    [InlineData(InspectionStatus.Pending)]
    [InlineData(InspectionStatus.Inspecting)]
    [InlineData(InspectionStatus.Failed)]
    public async Task Refuses_a_vehicle_that_has_not_passed_its_inspection(InspectionStatus inspection)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();

        try
        {
            await AddVehicleAsync(vin, inspection);

            var result = await repository.AssignVehicleAsync(id, vin, cancellationToken);

            result.Should().Be(AssignVehicleResult.NotPassedInspection);
            (await ConvoyOfAsync(vin)).Should().Be(DBNull.Value);
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
        // Otherwise assigning to a second convoy would silently empty a seat on the first —
        // including one whose truck list is already published and manifested.
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var first = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);
        var second = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();

        try
        {
            await AddVehicleAsync(vin);
            await repository.AssignVehicleAsync(first, vin, cancellationToken);

            var result = await repository.AssignVehicleAsync(second, vin, cancellationToken);

            result.Should().Be(AssignVehicleResult.OnAnotherConvoy);
            (await ConvoyOfAsync(vin)).Should().Be(first);
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(first);
            await RemoveConvoyAsync(second);
        }
    }

    [Fact]
    public async Task Cancelling_a_convoy_releases_its_vehicles_rather_than_deleting_them()
    {
        // Vehicles are themselves part of the aid. A cancelled convoy must not take donated
        // vehicles out of the system with it — the foreign key is ON DELETE SET NULL for this.
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();

        try
        {
            await AddVehicleAsync(vin);
            await repository.AssignVehicleAsync(id, vin, cancellationToken);

            await repository.DeleteAsync(id, cancellationToken);

            (await ConvoyOfAsync(vin)).Should().Be(DBNull.Value);
        }
        finally
        {
            await RemoveVehicleAsync(vin);
        }
    }
}

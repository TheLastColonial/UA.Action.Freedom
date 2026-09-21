using AwesomeAssertions;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Data.Convoys;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.Convoys.ConvoyFixtures;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Convoys;

/// <summary>
/// The Dapper <see cref="ConvoyRepository"/> against a real database — the convoy as a
/// <em>journey</em>: its route, the publication of its truck list, and its arrival. The truck list
/// itself, its crew and its insurance are <see cref="ConvoyVehicleRepositoryTests"/>.
/// </summary>
/// <remarks>
/// Needs the local stack up (<c>iac/local</c> + <c>tofu apply</c>) or a
/// <c>ConnectionStrings__Freedom</c> pointing at an equivalent database; skips itself otherwise.
///
/// <para>
/// Three things here can only be tested against a real database: the route transaction, the
/// arrival transaction, and the foreign keys that refuse to erase a convoy a manifest names while
/// still releasing donated vehicles rather than deleting them.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public class ConvoyRepositoryTests
{
    private static async Task<(ConvoyRepository Convoys, ConvoyVehicleRepository TruckList)> ConnectOrSkipAsync(
        CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync(Probe, cancellationToken);
        return (new ConvoyRepository(ConnectionFactory()), new ConvoyVehicleRepository(ConnectionFactory()));
    }

    private static RouteStopReadModel AStop(int sequence, string city, string postcode) =>
        new(sequence, "Unit 4", "Cross Road", city, "United Kingdom", postcode);

    [Fact]
    public async Task Round_trips_a_convoy_and_hands_back_the_identifier_it_assigned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, _) = await ConnectOrSkipAsync(cancellationToken);

        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            id.Should().BeGreaterThan(0);

            var stored = await convoys.GetByIdAsync(id, cancellationToken);

            stored.Should().Be(new ConvoyReadModel(id, Start, ExpectedEnd, TruckListPublishedAt: null));
            stored!.TruckListPublished.Should().BeFalse();
            stored.Arrived.Should().BeFalse();
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
        var (convoys, _) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            await convoys.ReplaceRouteAsync(
                id, [AStop(1, "Coventry", "CV1 2AB"), AStop(2, "Warszawa", "80-180")], cancellationToken);

            var route = await convoys.GetRouteAsync(id, cancellationToken);
            route.Select(stop => stop.City).Should().ContainInOrder("Coventry", "Warszawa");

            // Replacing is a replacement, not an append: the old stops have to be gone.
            await convoys.ReplaceRouteAsync(id, [AStop(1, "Dover", "CT16 1JA")], cancellationToken);

            var replaced = await convoys.GetRouteAsync(id, cancellationToken);
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
        var (convoys, _) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);

        await convoys.ReplaceRouteAsync(id, [AStop(1, "Coventry", "CV1 2AB")], cancellationToken);

        (await convoys.DeleteAsync(id, cancellationToken)).Should().Be(DeleteResult.Deleted);

        // The cascade is what stops a cancelled convoy leaving orphan stops behind.
        (await convoys.GetRouteAsync(id, cancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task Publishes_a_truck_list_once_and_refuses_the_second_attempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, _) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            (await convoys.PublishTruckListAsync(id, DateTime.UtcNow, cancellationToken)).Should().BeTrue();

            // The UPDATE is conditional on nothing having published yet, so the database — not
            // the application — is what settles a race between two dispatchers.
            (await convoys.PublishTruckListAsync(id, DateTime.UtcNow, cancellationToken)).Should().BeFalse();

            (await convoys.GetByIdAsync(id, cancellationToken))!.TruckListPublished.Should().BeTrue();
        }
        finally
        {
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task Arrival_hands_over_delivered_and_lost_vehicles_and_leaves_returned_ones_free()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var delivered = NewVin();
        var lost = NewVin();
        var returned = NewVin();
        var arrivedAt = new DateTime(2026, 9, 5, 17, 0, 0, DateTimeKind.Utc);

        try
        {
            foreach (var (vin, status) in new[]
                     {
                         (delivered, ManifestStatus.Delivered),
                         (lost, ManifestStatus.Lost),
                         (returned, ManifestStatus.Returned),
                     })
            {
                await AddVehicleAsync(vin);
                await truckList.AddAsync(id, vin, cancellationToken);
                await AddManifestAsync(id, vin, status);
            }

            await convoys.PublishTruckListAsync(id, DateTime.UtcNow, cancellationToken);

            var result = await convoys.ArriveAsync(id, arrivedAt, cancellationToken);

            result.Should().Be(ArriveResult.Arrived);
            (await convoys.GetByIdAsync(id, cancellationToken))!.ArrivedAt.Should().Be(arrivedAt);

            // Delivered and Lost vehicles are part of the aid and stay in Ukraine.
            (await HandedOverAtAsync(delivered)).Should().Be(arrivedAt);
            (await HandedOverAtAsync(lost)).Should().Be(arrivedAt);
            (await HandedOverAtAsync(returned)).Should().Be(DBNull.Value);

            // Nothing is released by clearing a pointer any more. The truck list still names every
            // vehicle that set off — which it could not do before, because arrival nulled the
            // pointer and the convoy lost its own list — and a Returned vehicle is free for the
            // next convoy simply because this one has arrived.
            (await TruckListCountAsync(id)).Should().Be(3);
            (await ConvoyOfAsync(returned)).Should().BeNull();
            (await ConvoyOfAsync(delivered)).Should().BeNull();

            (await convoys.ArriveAsync(id, arrivedAt, cancellationToken)).Should().Be(ArriveResult.AlreadyArrived);
        }
        finally
        {
            await RemoveManifestsAsync(id);
            await RemoveVehicleAsync(delivered);
            await RemoveVehicleAsync(lost);
            await RemoveVehicleAsync(returned);
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task Arrival_is_refused_while_any_vehicle_is_still_on_the_road()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var travelling = NewVin();
        var noManifest = NewVin();

        try
        {
            await AddVehicleAsync(travelling);
            await AddVehicleAsync(noManifest);
            await truckList.AddAsync(id, travelling, cancellationToken);
            await truckList.AddAsync(id, noManifest, cancellationToken);
            await AddManifestAsync(id, travelling, ManifestStatus.InTransit);
            await convoys.PublishTruckListAsync(id, DateTime.UtcNow, cancellationToken);

            var result = await convoys.ArriveAsync(id, DateTime.UtcNow, cancellationToken);

            result.Should().Be(ArriveResult.VehiclesStillTravelling);
            (await convoys.GetByIdAsync(id, cancellationToken))!.ArrivedAt.Should().BeNull();
            (await HandedOverAtAsync(travelling)).Should().Be(DBNull.Value);

            (await convoys.ListVehiclesStillTravellingAsync(id, cancellationToken))
                .Should().BeEquivalentTo([travelling, noManifest]);
        }
        finally
        {
            await RemoveManifestsAsync(id);
            await RemoveVehicleAsync(travelling);
            await RemoveVehicleAsync(noManifest);
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task A_vehicle_that_withdrew_does_not_hold_the_convoy_back()
    {
        // It broke down near Poznan and left. Waiting for it to deliver something would mean the
        // convoy could never arrive — and the rest of it did arrive.
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var arrived = NewVin();
        var brokeDown = NewVin();

        try
        {
            await AddVehicleAsync(arrived);
            await AddVehicleAsync(brokeDown);
            await truckList.AddAsync(id, arrived, cancellationToken);
            await truckList.AddAsync(id, brokeDown, cancellationToken);
            await AddManifestAsync(id, arrived, ManifestStatus.Delivered);
            await AddManifestAsync(id, brokeDown, ManifestStatus.InTransit);
            await convoys.PublishTruckListAsync(id, DateTime.UtcNow, cancellationToken);

            await truckList.WithdrawAsync(id, brokeDown, "Gearbox failure near Poznan", DateTime.UtcNow, cancellationToken);

            (await convoys.ListVehiclesStillTravellingAsync(id, cancellationToken)).Should().BeEmpty();
            (await convoys.ArriveAsync(id, DateTime.UtcNow, cancellationToken)).Should().Be(ArriveResult.Arrived);

            // The withdrawn vehicle is not handed over — whatever became of it did not become of
            // it here — and its manifest is untouched.
            (await HandedOverAtAsync(brokeDown)).Should().Be(DBNull.Value);
            (await ScalarAsync(
                "SELECT COUNT(1) FROM dbo.Manifest WHERE ConvoyId = @id AND Vin = @vin", ("@id", id), ("@vin", brokeDown)))
                .Should().Be(1);
        }
        finally
        {
            await RemoveManifestsAsync(id);
            await RemoveVehicleAsync(arrived);
            await RemoveVehicleAsync(brokeDown);
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task A_convoy_a_manifest_names_is_kept_and_reported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(id, vin, cancellationToken);
            await AddManifestAsync(id, vin, ManifestStatus.Created);

            (await convoys.DeleteAsync(id, cancellationToken)).Should().Be(DeleteResult.StillReferenced);
            (await convoys.ExistsAsync(id, cancellationToken)).Should().BeTrue();

            // The refusal rolled the truck-list delete back with it, so the vehicle is still on.
            (await ConvoyOfAsync(vin)).Should().Be(id);
        }
        finally
        {
            await RemoveManifestsAsync(id);
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task Cancelling_a_convoy_releases_its_vehicles_rather_than_deleting_them()
    {
        // Vehicles are themselves part of the aid. A cancelled convoy must not take donated
        // vehicles out of the system with it — it takes only their truck-list entries.
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(id, vin, cancellationToken);

            await convoys.DeleteAsync(id, cancellationToken);

            (await ScalarAsync("SELECT COUNT(1) FROM dbo.Vehicle WHERE Vin = @vin", ("@vin", vin))).Should().Be(1);
            (await ConvoyOfAsync(vin)).Should().BeNull();
            (await TruckListCountAsync(id)).Should().Be(0);
        }
        finally
        {
            await RemoveVehicleAsync(vin);
        }
    }

    [Fact]
    public async Task Cancelling_a_convoy_stands_its_crews_down()
    {
        // Otherwise the released vehicle carries the old crew to whichever convoy it joins next.
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var cancelled = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var next = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var driver = await AddDriverAsync("Olena", "Bondar");

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(cancelled, vin, cancellationToken);
            await truckList.AssignCrewAsync(cancelled, vin, driver, JourneyLeg.Uk, CrewRole.Driver, cancellationToken);

            await convoys.DeleteAsync(cancelled, cancellationToken);
            await truckList.AddAsync(next, vin, cancellationToken);

            (await truckList.ListCrewAsync(next, vin, leg: null, cancellationToken)).Should().BeEmpty();
            (await truckList.ListAsync(next, cancellationToken)).Should().ContainSingle()
                .Which.UkDriverCount.Should().Be(0);
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(next);
            await RemovePeopleAsync(driver);
        }
    }
}

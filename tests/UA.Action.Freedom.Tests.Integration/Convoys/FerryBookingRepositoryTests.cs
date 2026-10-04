using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Data.Convoys;
using static UA.Action.Freedom.Tests.Integration.Convoys.ConvoyFixtures;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Convoys;

/// <summary>A vehicle's outbound ferry booking against a real database (P1).</summary>
[Trait("Category", "Integration")]
public class FerryBookingRepositoryTests
{
    private static async Task<(ConvoyRepository Convoys, ConvoyVehicleRepository TruckList)> ConnectOrSkipAsync(
        CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync(Probe, cancellationToken);
        await EnsureRecorderAsync();
        return (new ConvoyRepository(ConnectionFactory(), Unattributed), new ConvoyVehicleRepository(ConnectionFactory(), Unattributed));
    }

    private static FerryBookingRecord ABooking(int convoyId, string vin, string reference = "POF-1") => new(
        convoyId, vin, "P&O Ferries", reference, new DateTime(2026, 9, 2, 7, 30, 0, DateTimeKind.Utc), "Freight", 310.00m);

    [Fact]
    public async Task Books_replaces_and_cancels_a_ferry_for_a_vehicle_on_the_convoy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(id, vin, cancellationToken);

            (await truckList.RecordFerryBookingAsync(ABooking(id, vin), cancellationToken)).Should().BeTrue();
            (await truckList.RecordFerryBookingAsync(ABooking(id, vin, "POF-2"), cancellationToken)).Should().BeTrue();

            var booking = await truckList.GetFerryBookingAsync(id, vin, cancellationToken);
            booking!.Reference.Should().Be("POF-2");
            booking.CostGbp.Should().Be(310.00m);
            (await ScalarAsync("SELECT COUNT(1) FROM dbo.ConvoyVehicleFerryBooking WHERE ConvoyId = @id", ("@id", id)))
                .Should().Be(1);

            (await truckList.RemoveFerryBookingAsync(id, vin, cancellationToken)).Should().BeTrue();
            (await truckList.GetFerryBookingAsync(id, vin, cancellationToken)).Should().BeNull();
            (await truckList.RemoveFerryBookingAsync(id, vin, cancellationToken)).Should().BeFalse();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task A_withdrawn_vehicle_keeps_its_booking_but_takes_no_new_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(id, vin, cancellationToken);
            await truckList.RecordFerryBookingAsync(ABooking(id, vin), cancellationToken);
            await truckList.WithdrawAsync(id, vin, "Accident", DateTime.UtcNow, cancellationToken);

            (await truckList.GetFerryBookingAsync(id, vin, cancellationToken)).Should().NotBeNull();
            (await truckList.RecordFerryBookingAsync(ABooking(id, vin, "POF-9"), cancellationToken)).Should().BeFalse();
            (await truckList.RecordFerryBookingAsync(ABooking(id, "NOSUCHVIN000000"), cancellationToken)).Should().BeFalse();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task Taking_the_vehicle_off_the_truck_list_takes_its_booking_with_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(id, vin, cancellationToken);
            await truckList.RecordFerryBookingAsync(ABooking(id, vin), cancellationToken);

            await truckList.RemoveAsync(id, vin, cancellationToken);

            (await truckList.GetFerryBookingAsync(id, vin, cancellationToken)).Should().BeNull();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
        }
    }
}

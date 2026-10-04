using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Data.Convoys;
using UA.Action.Freedom.Data.Donations;
using static UA.Action.Freedom.Tests.Integration.Convoys.ConvoyFixtures;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Convoys;

/// <summary>Equipment the charity buys for a vehicle, against a real database (O13).</summary>
[Trait("Category", "Integration")]
public class VehicleEquipmentRepositoryTests
{
    private const string EquipmentProbe =
        """
        SELECT COUNT(1) FROM dbo.ConvoyVehicle;
        SELECT COUNT(1) FROM dbo.EquipmentItem;
        SELECT COUNT(1) FROM dbo.ConvoyVehicleEquipment;
        """;

    private static async Task<(ConvoyRepository Convoys, ConvoyVehicleRepository TruckList, VehicleEquipmentRepository Equipment)>
        ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync(EquipmentProbe, cancellationToken);
        return (
            new ConvoyRepository(ConnectionFactory(), Unattributed),
            new ConvoyVehicleRepository(ConnectionFactory(), Unattributed),
            new VehicleEquipmentRepository(ConnectionFactory(), Unattributed));
    }

    private static string NewName() => "IT Equipment " + Guid.NewGuid().ToString("N")[..12];

    private static Task RemoveItemAsync(int id) =>
        ExecuteAsync("DELETE FROM dbo.ConvoyVehicleEquipment WHERE EquipmentItemId = @id; DELETE FROM dbo.EquipmentItem WHERE Id = @id", ("@id", id));

    [Fact]
    public async Task The_catalogue_refuses_a_name_it_already_holds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, _, equipment) = await ConnectOrSkipAsync(cancellationToken);
        var name = NewName();
        var id = await equipment.AddItemAsync(name, 6.50m, cancellationToken);

        try
        {
            id.Should().NotBeNull();
            (await equipment.AddItemAsync(name.ToUpperInvariant(), null, cancellationToken)).Should().BeNull();
            (await equipment.ListItemsAsync(cancellationToken)).Should().Contain(item => item.Id == id && item.UnitCostGbp == 6.50m);
        }
        finally
        {
            await RemoveItemAsync(id!.Value);
        }
    }

    [Fact]
    public async Task Replacing_a_vehicles_equipment_leaves_exactly_the_lines_given_and_prices_them()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, equipment) = await ConnectOrSkipAsync(cancellationToken);
        var convoyId = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var triangle = (await equipment.AddItemAsync(NewName(), 6.50m, cancellationToken))!.Value;
        var strap = (await equipment.AddItemAsync(NewName(), null, cancellationToken))!.Value;

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(convoyId, vin, cancellationToken);

            (await equipment.ReplaceForVehicleAsync(
                convoyId, vin, [new VehicleEquipmentLine(triangle, 2, null), new VehicleEquipmentLine(strap, 1, 12m)], cancellationToken))
                .Should().Be(ReplaceEquipmentResult.Replaced);
            (await equipment.ReplaceForVehicleAsync(
                convoyId, vin, [new VehicleEquipmentLine(triangle, 3, null)], cancellationToken))
                .Should().Be(ReplaceEquipmentResult.Replaced);

            var lines = await equipment.ListForVehicleAsync(convoyId, vin, cancellationToken);
            var onConvoy = await equipment.ListForConvoyAsync(convoyId, cancellationToken);

            lines.Should().ContainSingle().Which.CountedCostGbp.Should().Be(19.50m);
            onConvoy.Should().BeEquivalentTo(lines);
        }
        finally
        {
            await RemoveItemAsync(triangle);
            await RemoveItemAsync(strap);
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }

    [Fact]
    public async Task An_unknown_item_or_a_vehicle_off_the_convoy_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, equipment) = await ConnectOrSkipAsync(cancellationToken);
        var convoyId = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var triangle = (await equipment.AddItemAsync(NewName(), 6.50m, cancellationToken))!.Value;

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(convoyId, vin, cancellationToken);
            await equipment.ReplaceForVehicleAsync(convoyId, vin, [new VehicleEquipmentLine(triangle, 1, null)], cancellationToken);

            (await equipment.ReplaceForVehicleAsync(
                convoyId, vin, [new VehicleEquipmentLine(triangle, 9, null), new VehicleEquipmentLine(int.MaxValue, 1, null)], cancellationToken))
                .Should().Be(ReplaceEquipmentResult.UnknownItem);
            (await equipment.ReplaceForVehicleAsync(
                convoyId, "NOSUCHVIN000000", [new VehicleEquipmentLine(triangle, 1, null)], cancellationToken))
                .Should().Be(ReplaceEquipmentResult.VehicleNotOnConvoy);

            (await equipment.ListForVehicleAsync(convoyId, vin, cancellationToken)).Should().ContainSingle()
                .Which.Quantity.Should().Be(1);
        }
        finally
        {
            await RemoveItemAsync(triangle);
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }

    [Fact]
    public async Task Taking_the_vehicle_off_the_truck_list_takes_its_equipment_with_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, equipment) = await ConnectOrSkipAsync(cancellationToken);
        var convoyId = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var triangle = (await equipment.AddItemAsync(NewName(), 6.50m, cancellationToken))!.Value;

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(convoyId, vin, cancellationToken);
            await equipment.ReplaceForVehicleAsync(convoyId, vin, [new VehicleEquipmentLine(triangle, 1, null)], cancellationToken);

            await truckList.RemoveAsync(convoyId, vin, cancellationToken);

            (await equipment.ListForVehicleAsync(convoyId, vin, cancellationToken)).Should().BeEmpty();
        }
        finally
        {
            await RemoveItemAsync(triangle);
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }

    [Fact]
    public async Task Equipment_is_not_part_of_the_value_delivered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, equipment) = await ConnectOrSkipAsync(cancellationToken);
        var donors = new DonorRepository(ConnectionFactory(), Unattributed);
        var donations = new DonationRepository(ConnectionFactory(), Unattributed);
        var donorId = Guid.NewGuid();
        await donors.AddAsync(new Application.Donations.DonorReadModel(donorId, "IT Donor " + NewName(), null, null), cancellationToken);
        var donationId = await donations.AddAsync(donorId, new DateOnly(2026, 9, 1), null, cancellationToken);
        var categoryId = await AddCategoryAsync();
        var boxId = await ScalarAsync("INSERT INTO dbo.Box (WeightKg) VALUES (0); SELECT CAST(SCOPE_IDENTITY() AS int);");
        await ExecuteAsync(
            """
            INSERT INTO dbo.BoxItem (Id, BoxId, CategoryId, Description, DonationId, Quantity, ValueGbp, ValueSource)
            VALUES (NEWID(), @box, @category, 'Tins', @donation, 12, 30.00, 0)
            """,
            ("@box", boxId), ("@category", categoryId), ("@donation", donationId));
        var convoyId = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var triangle = (await equipment.AddItemAsync(NewName(), 500m, cancellationToken))!.Value;

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(convoyId, vin, cancellationToken);
            await equipment.ReplaceForVehicleAsync(convoyId, vin, [new VehicleEquipmentLine(triangle, 4, null)], cancellationToken);

            var report = await donations.ReportItemsAsync(donorId, cancellationToken);

            report.Sum(item => item.ValueGbp ?? 0m).Should().Be(30.00m);
        }
        finally
        {
            await RemoveItemAsync(triangle);
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
            await ExecuteAsync("DELETE FROM dbo.Box WHERE Id = @id", ("@id", boxId));
            await ExecuteAsync("DELETE FROM dbo.Donation WHERE DonorId = @id; DELETE FROM dbo.Donor WHERE Id = @id", ("@id", donorId));
            await RemoveCategoryAsync(categoryId);
        }
    }
}

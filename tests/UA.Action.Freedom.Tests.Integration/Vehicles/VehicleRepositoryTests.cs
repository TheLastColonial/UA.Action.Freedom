using AwesomeAssertions;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Vehicles;
using UA.Action.Freedom.Data.Vehicles;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Vehicles;

/// <summary>
/// The Dapper <see cref="VehicleRepository"/> against a real <c>dbo.Vehicle</c>. Needs the
/// local stack up (<c>iac/local</c> + <c>tofu apply</c>) or a <c>ConnectionStrings__Freedom</c>
/// pointing at an equivalent database; skips itself otherwise, so it is safe in CI until a
/// SQL service container is added there.
/// </summary>
[Trait("Category", "Integration")]
public class VehicleRepositoryTests
{
    private static async Task<VehicleRepository> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync("SELECT COUNT(1) FROM dbo.Vehicle", cancellationToken);
        return new VehicleRepository(ConnectionFactory());
    }

    private static VehicleReadModel AVehicle(string vin) => new(
        Vin: vin,
        Plate: "IT12ABC",
        Brand: "Ford",
        Model: "Transit",
        Colour: "Silver",
        Transmission: TransmissionType.Manual,
        Notes: "Integration test row",
        Mileage: 120_000,
        Servicing: false,
        Year: 2015,
        Fuel: FuelType.Diesel,
        ConvoyId: null,
        PurchaserName: "operator",
        PurchaseDate: new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc),
        WeightKg: 1_800,
        MaxCargoWeightKg: 900.50m,
        CargoWidthCm: 150.25m,
        CargoDepthCm: 300.00m,
        CargoHeightCm: 180.75m);

    private static string NewVin() => "IT" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();

    private static Task RemoveAsync(string vin) =>
        ExecuteAsync("DELETE FROM dbo.Vehicle WHERE Vin = @vin", ("@vin", vin));

    [Fact]
    public async Task Round_trips_every_field_through_the_database()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var vin = NewVin();

        try
        {
            await repository.AddAsync(AVehicle(vin), cancellationToken);

            var stored = await repository.GetByVinAsync(vin, cancellationToken);

            stored.Should().Be(AVehicle(vin));
        }
        finally
        {
            await RemoveAsync(vin);
        }
    }

    [Fact]
    public async Task Update_changes_the_row_and_reports_whether_one_matched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var vin = NewVin();

        try
        {
            await repository.AddAsync(AVehicle(vin), cancellationToken);

            var changed = AVehicle(vin) with { Plate = "IT99ZZZ", WeightKg = 1_950, Servicing = true };
            var updated = await repository.UpdateAsync(changed, cancellationToken);
            var missing = await repository.UpdateAsync(AVehicle(NewVin()), cancellationToken);

            updated.Should().BeTrue();
            missing.Should().BeFalse();
            (await repository.GetByVinAsync(vin, cancellationToken)).Should().Be(changed);
        }
        finally
        {
            await RemoveAsync(vin);
        }
    }

    [Fact]
    public async Task Exists_and_Delete_track_the_row_lifecycle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var vin = NewVin();

        try
        {
            await repository.AddAsync(AVehicle(vin), cancellationToken);

            (await repository.ExistsAsync(vin, cancellationToken)).Should().BeTrue();
            (await repository.DeleteAsync(vin, cancellationToken)).Should().Be(DeleteResult.Deleted);
            (await repository.ExistsAsync(vin, cancellationToken)).Should().BeFalse();
            (await repository.DeleteAsync(vin, cancellationToken)).Should().Be(DeleteResult.NotFound);
        }
        finally
        {
            await RemoveAsync(vin);
        }
    }

    [Fact]
    public async Task A_new_vehicle_is_awaiting_inspection_with_no_notes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var vin = NewVin();

        try
        {
            await repository.AddAsync(AVehicle(vin), cancellationToken);

            var stored = await repository.GetByVinAsync(vin, cancellationToken);

            stored!.InspectionStatus.Should().Be(InspectionStatus.Pending);
            stored.InspectionNotes.Should().BeNull();
        }
        finally
        {
            await RemoveAsync(vin);
        }
    }

    [Fact]
    public async Task Recording_an_inspection_persists_its_status_and_notes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var vin = NewVin();

        try
        {
            await repository.AddAsync(AVehicle(vin), cancellationToken);

            var recorded = await repository.RecordInspectionAsync(
                vin, InspectionStatus.Failed, "Rear brake pads worn", cancellationToken);
            var missing = await repository.RecordInspectionAsync(
                NewVin(), InspectionStatus.Passed, null, cancellationToken);

            recorded.Should().BeTrue();
            missing.Should().BeFalse();
            var stored = await repository.GetByVinAsync(vin, cancellationToken);
            stored!.InspectionStatus.Should().Be(InspectionStatus.Failed);
            stored.InspectionNotes.Should().Be("Rear brake pads worn");
        }
        finally
        {
            await RemoveAsync(vin);
        }
    }

    [Fact]
    public async Task A_general_update_cannot_change_or_clear_an_inspection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var vin = NewVin();

        try
        {
            await repository.AddAsync(AVehicle(vin), cancellationToken);
            await repository.RecordInspectionAsync(vin, InspectionStatus.Passed, "All good", cancellationToken);

            await repository.UpdateAsync(
                AVehicle(vin) with { Plate = "IT99ZZZ", InspectionStatus = InspectionStatus.Pending, InspectionNotes = null },
                cancellationToken);

            var stored = await repository.GetByVinAsync(vin, cancellationToken);
            stored!.Plate.Should().Be("IT99ZZZ");
            stored.InspectionStatus.Should().Be(InspectionStatus.Passed);
            stored.InspectionNotes.Should().Be("All good");
        }
        finally
        {
            await RemoveAsync(vin);
        }
    }

    [Fact]
    public async Task Neither_adding_nor_editing_a_vehicle_can_change_its_convoy()
    {
        // Convoy membership belongs to /convoys/{id}/vehicles/{vin}, where the truck-list freeze,
        // the inspection gate and the crew clean-up live. A vehicle write must not route round them.
        //
        // It cannot, and no longer for want of a column in the UPDATE: dbo.Vehicle has no ConvoyId
        // at all. VehicleReadModel.ConvoyId is derived from dbo.ConvoyVehicle on the way out, so a
        // value sent in is read straight past.
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var vin = NewVin();
        var convoyId = await ScalarAsync(
            "INSERT INTO dbo.Convoy (Start, ExpectedEnd) VALUES ('2026-09-01', '2026-09-05'); SELECT CAST(SCOPE_IDENTITY() AS int);");

        try
        {
            await repository.AddAsync(AVehicle(vin) with { ConvoyId = convoyId }, cancellationToken);
            (await repository.GetByVinAsync(vin, cancellationToken))!.ConvoyId.Should().BeNull();

            await ExecuteAsync(
                "INSERT INTO dbo.ConvoyVehicle (ConvoyId, Vin) VALUES (@convoyId, @vin)",
                ("@convoyId", convoyId), ("@vin", vin));
            await repository.UpdateAsync(AVehicle(vin) with { ConvoyId = null, Plate = "IT99ZZZ" }, cancellationToken);

            var stored = await repository.GetByVinAsync(vin, cancellationToken);
            stored!.Plate.Should().Be("IT99ZZZ");
            stored.ConvoyId.Should().Be(convoyId);
        }
        finally
        {
            await RemoveAsync(vin);
            await ExecuteAsync(
                "DELETE FROM dbo.ConvoyVehicle WHERE ConvoyId = @id; DELETE FROM dbo.Convoy WHERE Id = @id",
                ("@id", convoyId));
        }
    }

    [Fact]
    public async Task An_arrived_convoy_keeps_its_truck_list_and_frees_the_vehicle()
    {
        // The hole the truck-list table closed. Arrival used to null dbo.Vehicle.ConvoyId to
        // release a vehicle, which meant an arrived convoy lost the record of what had travelled
        // on it. The entry now stays and the vehicle reads as free because the convoy has arrived.
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var vin = NewVin();
        var convoyId = await ScalarAsync(
            """
            INSERT INTO dbo.Convoy (Start, ExpectedEnd, TruckListPublishedAt, ArrivedAt)
            VALUES ('2026-09-01', '2026-09-05', '2026-08-20', '2026-09-05');
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """);

        try
        {
            await repository.AddAsync(AVehicle(vin), cancellationToken);
            await ExecuteAsync(
                "INSERT INTO dbo.ConvoyVehicle (ConvoyId, Vin) VALUES (@convoyId, @vin)",
                ("@convoyId", convoyId), ("@vin", vin));

            (await repository.GetByVinAsync(vin, cancellationToken))!.ConvoyId.Should().BeNull();
            (await ScalarAsync(
                "SELECT COUNT(1) FROM dbo.ConvoyVehicle WHERE ConvoyId = @id", ("@id", convoyId))).Should().Be(1);
        }
        finally
        {
            await RemoveAsync(vin);
            await ExecuteAsync(
                "DELETE FROM dbo.ConvoyVehicle WHERE ConvoyId = @id; DELETE FROM dbo.Convoy WHERE Id = @id",
                ("@id", convoyId));
        }
    }

    [Fact]
    public async Task A_vehicle_a_manifest_names_is_kept_and_reported()
    {
        // The manifest is the record of what that vehicle carried; it outlives any tidy-up.
        // The refusal now comes through the truck-list entry: deleting the vehicle would cascade
        // that entry away, and FK_Manifest_ConvoyVehicle is NO ACTION.
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var vin = NewVin();
        var manifestId = "IT" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var convoyId = await ScalarAsync(
            "INSERT INTO dbo.Convoy (Start, ExpectedEnd) VALUES ('2026-09-01', '2026-09-05'); SELECT CAST(SCOPE_IDENTITY() AS int);");

        try
        {
            await repository.AddAsync(AVehicle(vin), cancellationToken);
            await ExecuteAsync(
                """
                INSERT INTO dbo.ConvoyVehicle (ConvoyId, Vin) VALUES (@convoyId, @vin);
                INSERT INTO dbo.Manifest (Id, ConvoyId, Vin) VALUES (@id, @convoyId, @vin);
                """,
                ("@id", manifestId), ("@convoyId", convoyId), ("@vin", vin));

            (await repository.DeleteAsync(vin, cancellationToken)).Should().Be(DeleteResult.StillReferenced);
            (await repository.ExistsAsync(vin, cancellationToken)).Should().BeTrue();
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.Manifest WHERE Id = @id", ("@id", manifestId));
            await RemoveAsync(vin);
            await ExecuteAsync(
                "DELETE FROM dbo.ConvoyVehicle WHERE ConvoyId = @id; DELETE FROM dbo.Convoy WHERE Id = @id",
                ("@id", convoyId));
        }
    }

    [Fact]
    public async Task List_returns_a_stored_vehicle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var vin = NewVin();

        try
        {
            await repository.AddAsync(AVehicle(vin), cancellationToken);

            var page = await repository.ListAsync(1, 200, cancellationToken);

            page.Should().ContainSingle(v => v.Vin == vin);
        }
        finally
        {
            await RemoveAsync(vin);
        }
    }
}

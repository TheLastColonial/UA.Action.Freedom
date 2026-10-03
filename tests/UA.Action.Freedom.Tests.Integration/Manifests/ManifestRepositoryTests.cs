using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Data.Manifests;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Manifests;

/// <summary>
/// The Dapper <see cref="ManifestRepository"/> against real manifest tables. Skips itself when
/// the local stack is not up.
/// </summary>
/// <remarks>
/// The conditional writes are what need a real database: <c>TransitionAsync</c> and
/// <c>ConfirmAndFreezeAsync</c> both depend on the <c>WHERE</c> clause deciding a race, and
/// <c>ConfirmAndFreezeAsync</c> additionally has to confirm and freeze in one statement — a
/// manifest that is Confirmed but not yet frozen is editable, and that window is what §5.2 rules
/// out.
///
/// <para>
/// Every manifest here is opened against a real truck-list entry, because <c>ConvoyId</c> and
/// <c>Vin</c> are NOT NULL and are a composite foreign key to <c>dbo.ConvoyVehicle</c>. That is
/// the change: a manifest naming a vehicle that is on no convoy, or on a different one, is no
/// longer a state the database can be in.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public class ManifestRepositoryTests
{
    private static async Task<ManifestRepository> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync(
            "SELECT COUNT(1) FROM dbo.Manifest; SELECT COUNT(1) FROM dbo.ManifestBox; SELECT COUNT(1) FROM dbo.ConvoyVehicle;",
            cancellationToken);
        return new ManifestRepository(ConnectionFactory(), Unattributed);
    }

    private static string NewId() => "IT" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    private static string NewVin() => "IT" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();

    /// <summary>One vehicle on one convoy — the truck-list entry a manifest belongs to.</summary>
    private sealed record TruckListEntry(int ConvoyId, string Vin, string Plate);

    private static ManifestReadModel AManifest(string id, TruckListEntry on) => new(
        id, on.ConvoyId, on.Vin, ManifestStatus.Created,
        DeliveryNotes: "Integration test", FerryBookingComplete: false, GmrSubmittedAt: null);

    /// <summary>
    /// A convoy with one vehicle on its truck list. The capacity columns are optional — nothing
    /// back-fills them, and a vehicle nobody has measured cannot be judged overloaded.
    /// </summary>
    private static async Task<TruckListEntry> ATruckListEntryAsync(
        decimal? maxCargoWeightKg = null,
        decimal? widthCm = null,
        decimal? depthCm = null,
        decimal? heightCm = null,
        string plate = "IT12ABC")
    {
        var vin = NewVin();

        await ExecuteAsync(
            """
            INSERT INTO dbo.Vehicle (Vin, Plate, [Year], WeightKg, InspectionStatus, MaxCargoWeightKg, CargoWidthCm, CargoDepthCm, CargoHeightCm)
            VALUES (@vin, @plate, 2016, 1800, 2, @maxCargoWeightKg, @widthCm, @depthCm, @heightCm);
            """,
            ("@vin", vin),
            ("@plate", plate),
            ("@maxCargoWeightKg", (object?)maxCargoWeightKg ?? DBNull.Value),
            ("@widthCm", (object?)widthCm ?? DBNull.Value),
            ("@depthCm", (object?)depthCm ?? DBNull.Value),
            ("@heightCm", (object?)heightCm ?? DBNull.Value));

        var convoyId = Convert.ToInt32(await ValueAsync(
            """
            INSERT INTO dbo.Convoy (Start, ExpectedEnd) VALUES ('2026-09-01T06:00:00', '2026-09-05T18:00:00');
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """));

        await ExecuteAsync(
            "INSERT INTO dbo.ConvoyVehicle (ConvoyId, Vin) VALUES (@convoyId, @vin)",
            ("@convoyId", convoyId), ("@vin", vin));

        return new TruckListEntry(convoyId, vin, plate);
    }

    /// <summary>
    /// Takes the scaffold down in foreign-key order: the vehicle cascades its truck-list row, and
    /// the convoy goes last. Any manifest naming it must already be gone — that is NO ACTION.
    /// </summary>
    private static async Task RemoveTruckListEntryAsync(TruckListEntry entry)
    {
        await ExecuteAsync("DELETE FROM dbo.Vehicle WHERE Vin = @vin", ("@vin", entry.Vin));
        await ExecuteAsync("DELETE FROM dbo.Convoy WHERE Id = @id", ("@id", entry.ConvoyId));
    }

    private static Task<Guid> AddVolunteerAsync(bool isDriver = true) =>
        SqlTestDatabase.AddVolunteerAsync("Integration", "Driver", isDriver);

    private static async Task<int> AddBoxAsync(
        int weightKg, bool validated, Guid? validatedBy, decimal? widthCm = null, decimal? depthCm = null, decimal? heightCm = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO dbo.Box (WeightKg, WidthCm, DepthCm, HeightCm, ValidatedByPersonId, ValidatedAt)
            VALUES (@weightKg, @widthCm, @depthCm, @heightCm, @validatedBy, @validatedAt);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;
        command.Parameters.AddWithValue("@weightKg", weightKg);
        command.Parameters.AddWithValue("@widthCm", (object?)widthCm ?? DBNull.Value);
        command.Parameters.AddWithValue("@depthCm", (object?)depthCm ?? DBNull.Value);
        command.Parameters.AddWithValue("@heightCm", (object?)heightCm ?? DBNull.Value);
        command.Parameters.AddWithValue("@validatedBy", validated ? validatedBy! : DBNull.Value);
        command.Parameters.AddWithValue("@validatedAt", validated ? DateTime.UtcNow : DBNull.Value);

        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static Task RemoveManifestAsync(string id) =>
        ExecuteAsync("DELETE FROM dbo.Manifest WHERE Id = @id", ("@id", id));

    private static Task RemoveBoxAsync(int id) =>
        ExecuteAsync("DELETE FROM dbo.Box WHERE Id = @id", ("@id", id));

    private static Task RemoveVolunteerAsync(Guid id) =>
        ExecuteAsync("DELETE FROM dbo.Person WHERE Id = @id", ("@id", id));

    [Fact]
    public async Task Round_trips_a_manifest_in_the_created_state()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var entry = await ATruckListEntryAsync();
        var id = NewId();

        try
        {
            await repository.AddAsync(AManifest(id, entry), cancellationToken);

            var stored = await repository.GetByIdAsync(id, cancellationToken);

            stored.Should().Be(AManifest(id, entry) with { LastChangedAt = stored!.LastChangedAt });
            stored!.Frozen.Should().BeFalse();

            // The pair is also reachable from the truck-list side: one manifest per vehicle per convoy.
            (await repository.GetForVehicleAsync(entry.ConvoyId, entry.Vin, cancellationToken))!.Id.Should().Be(id);
        }
        finally
        {
            await RemoveManifestAsync(id);
            await RemoveTruckListEntryAsync(entry);
        }
    }

    [Fact]
    public async Task A_transition_only_fires_from_the_state_it_expected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var entry = await ATruckListEntryAsync();
        var id = NewId();

        try
        {
            await repository.AddAsync(AManifest(id, entry), cancellationToken);

            (await repository.TransitionAsync(id, ManifestStatus.Created, ManifestStatus.Proposed, cancellationToken))
                .Should().BeTrue();

            // The same call again finds nothing to move: the manifest is no longer in Created.
            // That is how two dispatchers pressing one button resolve to a single transition.
            (await repository.TransitionAsync(id, ManifestStatus.Created, ManifestStatus.Proposed, cancellationToken))
                .Should().BeFalse();

            (await repository.GetByIdAsync(id, cancellationToken))!.Status.Should().Be(ManifestStatus.Proposed);
        }
        finally
        {
            await RemoveManifestAsync(id);
            await RemoveTruckListEntryAsync(entry);
        }
    }

    [Fact]
    public async Task Confirming_and_freezing_happen_in_one_write()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var entry = await ATruckListEntryAsync();
        var id = NewId();

        try
        {
            await repository.AddAsync(AManifest(id, entry) with { Status = ManifestStatus.Proposed }, cancellationToken);

            var stamped = await repository.ConfirmAndFreezeAsync(id, ManifestStatus.Proposed, cancellationToken);

            stamped.Should().NotBeNull();

            var stored = await repository.GetByIdAsync(id, cancellationToken);
            stored!.Status.Should().Be(ManifestStatus.Confirmed);
            stored.Frozen.Should().BeTrue();
            stored.GmrSubmittedAt.Should().Be(stamped);

            // A second approval finds nothing: one manifest, one GMR.
            (await repository.ConfirmAndFreezeAsync(id, ManifestStatus.Proposed, cancellationToken))
                .Should().BeNull();
        }
        finally
        {
            await RemoveManifestAsync(id);
            await RemoveTruckListEntryAsync(entry);
        }
    }

    [Fact]
    public async Task An_update_cannot_reach_the_status_the_freeze_or_the_vehicle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var entry = await ATruckListEntryAsync();
        var elsewhere = await ATruckListEntryAsync();
        var id = NewId();

        try
        {
            await repository.AddAsync(AManifest(id, entry) with { Status = ManifestStatus.Proposed }, cancellationToken);
            await repository.ConfirmAndFreezeAsync(id, ManifestStatus.Proposed, cancellationToken);

            // Asked directly to unfreeze, rewind, and re-point at a different vehicle on a
            // different convoy. The UPDATE has no columns for any of it.
            await repository.UpdateAsync(
                AManifest(id, elsewhere) with
                {
                    Status = ManifestStatus.Created,
                    GmrSubmittedAt = null,
                    DeliveryNotes = "changed",
                },
                cancellationToken);

            var stored = await repository.GetByIdAsync(id, cancellationToken);
            stored!.Status.Should().Be(ManifestStatus.Confirmed);
            stored.Frozen.Should().BeTrue();
            stored.DeliveryNotes.Should().Be("changed");
            stored.ConvoyId.Should().Be(entry.ConvoyId);
            stored.Vin.Should().Be(entry.Vin);
        }
        finally
        {
            await RemoveManifestAsync(id);
            await RemoveTruckListEntryAsync(entry);
            await RemoveTruckListEntryAsync(elsewhere);
        }
    }

    [Fact]
    public async Task A_box_travels_on_at_most_one_manifest()
    {
        // Counted twice at a border and arriving once is the failure this prevents.
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var firstEntry = await ATruckListEntryAsync();
        var secondEntry = await ATruckListEntryAsync();
        var first = NewId();
        var second = NewId();
        var boxId = await AddBoxAsync(30, validated: false, validatedBy: null);

        try
        {
            await repository.AddAsync(AManifest(first, firstEntry), cancellationToken);
            await repository.AddAsync(AManifest(second, secondEntry), cancellationToken);

            (await repository.AddBoxAsync(first, boxId, cancellationToken)).Should().BeTrue();
            (await repository.AddBoxAsync(second, boxId, cancellationToken)).Should().BeTrue();

            (await repository.ListBoxesAsync(first, cancellationToken)).Should().BeEmpty();
            (await repository.ListBoxesAsync(second, cancellationToken)).Should().ContainSingle();

            (await repository.AddBoxAsync(first, 999_999, cancellationToken)).Should().BeFalse();
        }
        finally
        {
            await RemoveManifestAsync(first);
            await RemoveManifestAsync(second);
            await RemoveBoxAsync(boxId);
            await RemoveTruckListEntryAsync(firstEntry);
            await RemoveTruckListEntryAsync(secondEntry);
        }
    }

    [Fact]
    public async Task Cargo_reports_the_weight_and_validation_state_of_each_box()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var entry = await ATruckListEntryAsync();
        var id = NewId();
        var loader = await AddVolunteerAsync();
        var weighed = await AddBoxAsync(30, validated: true, validatedBy: loader, widthCm: 40m, depthCm: 30m, heightCm: 20m);
        var unweighed = await AddBoxAsync(0, validated: false, validatedBy: null);

        try
        {
            await repository.AddAsync(AManifest(id, entry), cancellationToken);
            await repository.AddBoxAsync(id, weighed, cancellationToken);
            await repository.AddBoxAsync(id, unweighed, cancellationToken);

            var cargo = await repository.ListBoxesAsync(id, cancellationToken);

            cargo.Should().HaveCount(2);
            cargo.Single(box => box.BoxId == weighed).Validated.Should().BeTrue();
            cargo.Single(box => box.BoxId == weighed).WeightKg.Should().Be(30);
            cargo.Single(box => box.BoxId == weighed).WidthCm.Should().Be(40m);
            cargo.Single(box => box.BoxId == weighed).DepthCm.Should().Be(30m);
            cargo.Single(box => box.BoxId == weighed).HeightCm.Should().Be(20m);
            cargo.Single(box => box.BoxId == unweighed).Validated.Should().BeFalse();
            cargo.Single(box => box.BoxId == unweighed).WidthCm.Should().BeNull();
        }
        finally
        {
            await RemoveManifestAsync(id);
            await RemoveBoxAsync(weighed);
            await RemoveBoxAsync(unweighed);
            await RemoveVolunteerAsync(loader);
            await RemoveTruckListEntryAsync(entry);
        }
    }

    [Fact]
    public async Task Vehicle_weight_capacity_and_plate_come_from_the_manifests_vehicle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var measured = await ATruckListEntryAsync(
            maxCargoWeightKg: 900.50m, widthCm: 150.25m, depthCm: 300m, heightCm: 180.75m, plate: "IT99XYZ");
        var unmeasured = await ATruckListEntryAsync();
        var id = NewId();
        var other = NewId();

        try
        {
            await repository.AddAsync(AManifest(id, measured), cancellationToken);
            await repository.AddAsync(AManifest(other, unmeasured), cancellationToken);

            var capacity = await repository.GetVehicleCargoCapacityAsync(id, cancellationToken);
            capacity.MaxCargoWeightKg.Should().Be(900.50m);
            capacity.CargoWidthCm.Should().Be(150.25m);
            capacity.CargoDepthCm.Should().Be(300m);
            capacity.CargoHeightCm.Should().Be(180.75m);

            (await repository.GetVehicleWeightKgAsync(id, cancellationToken)).Should().Be(1800);

            // The plate, not the VIN — this is what the GMR submission and the printed document
            // are documented to carry, and what was being fed the chassis number instead.
            (await repository.GetVehiclePlateAsync(id, cancellationToken)).Should().Be("IT99XYZ");

            // Nothing back-fills capacity, so a vehicle nobody has measured reports all-null
            // rather than failing — it simply cannot be judged overloaded.
            var unknown = await repository.GetVehicleCargoCapacityAsync(other, cancellationToken);
            unknown.MaxCargoWeightKg.Should().BeNull();
            unknown.CargoWidthCm.Should().BeNull();
        }
        finally
        {
            await RemoveManifestAsync(id);
            await RemoveManifestAsync(other);
            await RemoveTruckListEntryAsync(measured);
            await RemoveTruckListEntryAsync(unmeasured);
        }
    }

    [Fact]
    public async Task Deleting_a_manifest_leaves_its_boxes_alone()
    {
        // Boxes outlive the manifest that named them — a cancelled manifest must not delete
        // cargo that has already been packed and weighed.
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var entry = await ATruckListEntryAsync();
        var id = NewId();
        var boxId = await AddBoxAsync(30, validated: false, validatedBy: null);

        try
        {
            await repository.AddAsync(AManifest(id, entry), cancellationToken);
            await repository.AddBoxAsync(id, boxId, cancellationToken);

            (await repository.DeleteAsync(id, cancellationToken)).Should().BeTrue();

            (await ScalarAsync("SELECT COUNT(1) FROM dbo.Box WHERE Id = @id", ("@id", boxId))).Should().Be(1);
        }
        finally
        {
            await RemoveBoxAsync(boxId);
            await RemoveTruckListEntryAsync(entry);
        }
    }

    private static Task AddItemAsync(int boxId, int categoryId, string description, string? commodityCode) =>
        ExecuteAsync(
            """
            INSERT INTO dbo.BoxItem (Id, BoxId, CategoryId, Description, CommodityCode)
            VALUES (NEWID(), @boxId, @categoryId, @description, @commodityCode)
            """,
            ("@boxId", boxId),
            ("@categoryId", categoryId),
            ("@description", description),
            ("@commodityCode", (object?)commodityCode ?? DBNull.Value));

    [Fact]
    public async Task An_items_own_code_wins_and_otherwise_its_category_EU_code_is_declared()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var entry = await ATruckListEntryAsync();
        var id = NewId();
        var validatedBy = await AddVolunteerAsync();
        var boxId = await AddBoxAsync(30, validated: true, validatedBy);
        var mapped = await AddCategoryAsync(euCode: "300490");
        var unmapped = await AddCategoryAsync();

        try
        {
            await repository.AddAsync(AManifest(id, entry), cancellationToken);
            await repository.AddBoxAsync(id, boxId, cancellationToken);
            await AddItemAsync(boxId, mapped, "A inherits", commodityCode: null);
            await AddItemAsync(boxId, mapped, "B overrides", commodityCode: "99190000");
            await AddItemAsync(boxId, unmapped, "C has nothing", commodityCode: null);

            var lines = await repository.GetEnsGoodsLinesAsync(id, cancellationToken);

            lines.Single(line => line.ItemDescription == "A inherits").CommodityCode.Should().Be("300490");
            lines.Single(line => line.ItemDescription == "B overrides").CommodityCode.Should().Be("99190000");
            lines.Single(line => line.ItemDescription == "C has nothing").CommodityCode.Should().BeNull();
            lines.Single(line => line.ItemDescription == "C has nothing").CategoryName.Should().NotBeNullOrEmpty();
        }
        finally
        {
            await RemoveManifestAsync(id);
            await RemoveBoxAsync(boxId);
            await RemoveCategoryAsync(mapped);
            await RemoveCategoryAsync(unmapped);
            await RemoveVolunteerAsync(validatedBy);
            await RemoveTruckListEntryAsync(entry);
        }
    }

    /// <summary>
    /// The ICS2 filing sheet's source query, against real SQL.
    /// </summary>
    /// <remarks>
    /// Worth an integration test rather than trusting the fake for two reasons the in-memory
    /// repository cannot show. The row shape is per <em>item</em> while the weight is per
    /// <em>box</em>, so the caller has to de-duplicate on <c>BoxId</c> before adding anything up — a
    /// fake with one item per box would pass either way. And the goods item number is non-amendable
    /// in ICS2, so it comes from the query's <c>ORDER BY</c>; a test that did not put two items in one
    /// box could not tell a stable order from an accidental one.
    /// </remarks>
    [Fact]
    public async Task Reads_one_goods_line_per_item_with_the_boxs_weight_repeated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var entry = await ATruckListEntryAsync();
        var id = NewId();
        var validatedBy = await AddVolunteerAsync();
        var packed = await AddBoxAsync(30, validated: true, validatedBy);
        var unpacked = await AddBoxAsync(0, validated: false, validatedBy: null);
        var category = await AddCategoryAsync();

        try
        {
            await repository.AddAsync(AManifest(id, entry), cancellationToken);
            await repository.AddBoxAsync(id, packed, cancellationToken);
            await repository.AddBoxAsync(id, unpacked, cancellationToken);
            await AddItemAsync(packed, category, "Blankets", "99190000");
            await AddItemAsync(packed, category, "Sleeping bags", "99190000");
            await AddItemAsync(unpacked, category, "Assorted donations", commodityCode: null);

            var lines = await repository.GetEnsGoodsLinesAsync(id, cancellationToken);

            lines.Should().HaveCount(3);

            // The box's weight repeats across its items. Summing rows would say 60 kg for a 30 kg box.
            lines.Where(line => line.BoxId == packed).Should().HaveCount(2)
                .And.OnlyContain(line => line.WeightKg == 30);
            lines.DistinctBy(line => line.BoxId).Sum(line => line.WeightKg).Should().Be(30);

            // Stable order, because the goods item number is derived from it and cannot be amended.
            lines.Where(line => line.BoxId == packed).Select(line => line.ItemDescription)
                .Should().Equal("Blankets", "Sleeping bags");

            // Validation and the missing commodity code both come through, because the filing sheet
            // reports a provisional gross mass and an unclassified item by name.
            lines.Should().ContainSingle(line => line.ItemDescription == "Assorted donations")
                .Which.Should().Match<EnsGoodsLineReadModel>(line =>
                    !line.Validated && line.CommodityCode == null);
            lines.Should().Contain(line => line.CommodityCode == "99190000");
        }
        finally
        {
            await RemoveManifestAsync(id);
            await RemoveBoxAsync(packed);
            await RemoveBoxAsync(unpacked);
            await RemoveCategoryAsync(category);
            await RemoveVolunteerAsync(validatedBy);
            await RemoveTruckListEntryAsync(entry);
        }
    }
}

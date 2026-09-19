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
/// </remarks>
[Trait("Category", "Integration")]
public class ManifestRepositoryTests
{
    private static async Task<ManifestRepository> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync("SELECT COUNT(1) FROM dbo.Manifest; SELECT COUNT(1) FROM dbo.ManifestBox;", cancellationToken);
        return new ManifestRepository(ConnectionFactory());
    }

    private static string NewId() => "IT" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    private static ManifestReadModel AManifest(string id) => new(
        id, Vin: null, ConvoyId: null, ManifestStatus.Created,
        DeliveryNotes: "Integration test", FerryBookingComplete: false, GmrSubmittedAt: null);

    private static async Task<Guid> AddVolunteerAsync(bool isDriver = true)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT INTO dbo.Person (Id, FirstName, LastName, DateOfBirth, Joined, IsDriver)
            VALUES (@id, 'Integration', 'Driver', '1990-01-01', '2024-01-01', @isDriver)
            """,
            ("@id", id), ("@isDriver", isDriver));
        return id;
    }

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

    private static Task AddVehicleAsync(string vin, decimal? maxCargoWeightKg, decimal? widthCm, decimal? depthCm, decimal? heightCm) =>
        ExecuteAsync(
            """
            INSERT INTO dbo.Vehicle (Vin, Plate, [Year], MaxCargoWeightKg, CargoWidthCm, CargoDepthCm, CargoHeightCm)
            VALUES (@vin, 'IT12ABC', 2016, @maxCargoWeightKg, @widthCm, @depthCm, @heightCm)
            """,
            ("@vin", vin),
            ("@maxCargoWeightKg", (object?)maxCargoWeightKg ?? DBNull.Value),
            ("@widthCm", (object?)widthCm ?? DBNull.Value),
            ("@depthCm", (object?)depthCm ?? DBNull.Value),
            ("@heightCm", (object?)heightCm ?? DBNull.Value));

    private static Task RemoveVehicleAsync(string vin) =>
        ExecuteAsync("DELETE FROM dbo.Vehicle WHERE Vin = @vin", ("@vin", vin));

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
        var id = NewId();

        try
        {
            await repository.AddAsync(AManifest(id), cancellationToken);

            var stored = await repository.GetByIdAsync(id, cancellationToken);

            stored.Should().Be(AManifest(id));
            stored!.Frozen.Should().BeFalse();
        }
        finally
        {
            await RemoveManifestAsync(id);
        }
    }

    [Fact]
    public async Task A_transition_only_fires_from_the_state_it_expected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = NewId();

        try
        {
            await repository.AddAsync(AManifest(id), cancellationToken);

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
        }
    }

    [Fact]
    public async Task Confirming_and_freezing_happen_in_one_write()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = NewId();

        try
        {
            await repository.AddAsync(AManifest(id) with { Status = ManifestStatus.Proposed }, cancellationToken);

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
        }
    }

    [Fact]
    public async Task An_update_cannot_reach_the_status_or_the_freeze()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = NewId();

        try
        {
            await repository.AddAsync(AManifest(id) with { Status = ManifestStatus.Proposed }, cancellationToken);
            await repository.ConfirmAndFreezeAsync(id, ManifestStatus.Proposed, cancellationToken);

            // Asked directly to unfreeze and rewind. The UPDATE has no columns for either.
            await repository.UpdateAsync(
                AManifest(id) with { Status = ManifestStatus.Created, GmrSubmittedAt = null, DeliveryNotes = "changed" },
                cancellationToken);

            var stored = await repository.GetByIdAsync(id, cancellationToken);
            stored!.Status.Should().Be(ManifestStatus.Confirmed);
            stored.Frozen.Should().BeTrue();
            stored.DeliveryNotes.Should().Be("changed");
        }
        finally
        {
            await RemoveManifestAsync(id);
        }
    }

    [Fact]
    public async Task Crewing_a_leg_twice_replaces_the_team()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = NewId();
        var first = await AddVolunteerAsync();
        var second = await AddVolunteerAsync();

        try
        {
            await repository.AddAsync(AManifest(id), cancellationToken);

            await repository.SetTeamAsync(
                id, new ManifestDriverTeamReadModel(ManifestLeg.Uk, first, second), cancellationToken);
            await repository.SetTeamAsync(
                id, new ManifestDriverTeamReadModel(ManifestLeg.Uk, second, null), cancellationToken);

            // The primary key is (ManifestId, Leg), so a leg can never accumulate two crews.
            var teams = await repository.ListTeamsAsync(id, cancellationToken);
            var team = teams.Should().ContainSingle().Subject;
            team.PrimaryPersonId.Should().Be(second);
            team.SecondaryPersonId.Should().BeNull();
        }
        finally
        {
            await RemoveManifestAsync(id);
            await RemoveVolunteerAsync(first);
            await RemoveVolunteerAsync(second);
        }
    }

    [Fact]
    public async Task A_box_travels_on_at_most_one_manifest()
    {
        // Counted twice at a border and arriving once is the failure this prevents.
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var first = NewId();
        var second = NewId();
        var boxId = await AddBoxAsync(30, validated: false, validatedBy: null);

        try
        {
            await repository.AddAsync(AManifest(first), cancellationToken);
            await repository.AddAsync(AManifest(second), cancellationToken);

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
        }
    }

    [Fact]
    public async Task Cargo_reports_the_weight_and_validation_state_of_each_box()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = NewId();
        var loader = await AddVolunteerAsync();
        var weighed = await AddBoxAsync(30, validated: true, validatedBy: loader, widthCm: 40m, depthCm: 30m, heightCm: 20m);
        var unweighed = await AddBoxAsync(0, validated: false, validatedBy: null);

        try
        {
            await repository.AddAsync(AManifest(id), cancellationToken);
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
        }
    }

    [Fact]
    public async Task Vehicle_cargo_capacity_comes_from_the_assigned_vehicle_or_all_null_when_none_is_assigned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = NewId();
        var unassigned = NewId();
        var vin = "IT" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();

        try
        {
            await AddVehicleAsync(vin, maxCargoWeightKg: 900.50m, widthCm: 150.25m, depthCm: 300m, heightCm: 180.75m);
            await repository.AddAsync(AManifest(id) with { Vin = vin }, cancellationToken);
            await repository.AddAsync(AManifest(unassigned), cancellationToken);

            var capacity = await repository.GetVehicleCargoCapacityAsync(id, cancellationToken);
            var noVehicle = await repository.GetVehicleCargoCapacityAsync(unassigned, cancellationToken);

            capacity.MaxCargoWeightKg.Should().Be(900.50m);
            capacity.CargoWidthCm.Should().Be(150.25m);
            capacity.CargoDepthCm.Should().Be(300m);
            capacity.CargoHeightCm.Should().Be(180.75m);

            noVehicle.MaxCargoWeightKg.Should().BeNull();
            noVehicle.CargoWidthCm.Should().BeNull();
        }
        finally
        {
            await RemoveManifestAsync(id);
            await RemoveManifestAsync(unassigned);
            await RemoveVehicleAsync(vin);
        }
    }

    [Fact]
    public async Task Deleting_a_manifest_leaves_its_boxes_alone()
    {
        // Boxes outlive the manifest that named them — a cancelled manifest must not delete
        // cargo that has already been packed and weighed.
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = NewId();
        var boxId = await AddBoxAsync(30, validated: false, validatedBy: null);

        try
        {
            await repository.AddAsync(AManifest(id), cancellationToken);
            await repository.AddBoxAsync(id, boxId, cancellationToken);

            (await repository.DeleteAsync(id, cancellationToken)).Should().BeTrue();

            (await ScalarAsync("SELECT COUNT(1) FROM dbo.Box WHERE Id = @id", ("@id", boxId))).Should().Be(1);
        }
        finally
        {
            await RemoveBoxAsync(boxId);
        }
    }
}

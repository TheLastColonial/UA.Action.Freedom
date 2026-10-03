using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Locations;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Data.Boxes;
using UA.Action.Freedom.Data.Convoys;
using UA.Action.Freedom.Data.Locations;
using UA.Action.Freedom.Data.Receivers;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Receivers;

/// <summary>
/// Receiver registration (ADR 0012) against the real tables: the status round-trips and defaults to
/// pending, only <c>SetStatusAsync</c> can write it, a vehicle can name a handover receiver, a hub is a
/// flag on a location, and the usage read lists identifiers only.
/// </summary>
[Trait("Category", "Integration")]
public class ReceiverRegistrationRepositoryTests
{
    private static async Task SkipUnlessProvisionedAsync(CancellationToken cancellationToken) =>
        await SkipUnlessReachableAsync(
            "SELECT TOP 1 [Status] FROM dbo.Receiver; SELECT TOP 1 HandoverReceiverRef FROM dbo.ConvoyVehicle; SELECT TOP 1 IsRegisteredHub FROM dbo.Location",
            cancellationToken);

    private static ReceiverRepository Receivers() => new(ConnectionFactory(), Unattributed);

    private static Task RemoveReceiverAsync(Guid receiverRef) =>
        ExecuteAsync("DELETE FROM dbo.Receiver WHERE ReceiverRef = @r", ("@r", receiverRef));

    [Fact]
    public async Task A_new_receiver_is_pending_and_a_status_round_trips()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);
        var receiverRef = Guid.NewGuid();
        var repository = Receivers();

        try
        {
            await repository.AddAsync(
                new ReceiverReadModel(receiverRef, "Integration Hospital", "Kharkiv oblast", ReceiverStatus.Registered),
                CancellationToken.None);

            // The INSERT leaves Status to its default, whatever the caller built.
            (await repository.GetByRefAsync(receiverRef, CancellationToken.None))!.Status.Should().Be(ReceiverStatus.Pending);

            foreach (var status in Enum.GetValues<ReceiverStatus>())
            {
                (await repository.SetStatusAsync(receiverRef, status, CancellationToken.None)).Should().BeTrue();
                (await repository.GetByRefAsync(receiverRef, CancellationToken.None))!.Status.Should().Be(status);
            }
        }
        finally
        {
            await RemoveReceiverAsync(receiverRef);
        }
    }

    [Fact]
    public async Task An_ordinary_edit_leaves_the_status_alone()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);
        var receiverRef = Guid.NewGuid();
        var repository = Receivers();

        try
        {
            await repository.AddAsync(new ReceiverReadModel(receiverRef, "Integration Hospital", "Kharkiv oblast"), CancellationToken.None);
            await repository.SetStatusAsync(receiverRef, ReceiverStatus.Registered, CancellationToken.None);

            await repository.UpdateAsync(
                new ReceiverReadModel(receiverRef, "Integration Hospital No. 2", "Kharkiv oblast", ReceiverStatus.Pending),
                CancellationToken.None);

            var stored = await repository.GetByRefAsync(receiverRef, CancellationToken.None);
            stored!.Organisation.Should().Be("Integration Hospital No. 2");
            stored.Status.Should().Be(ReceiverStatus.Registered);
        }
        finally
        {
            await RemoveReceiverAsync(receiverRef);
        }
    }

    [Fact]
    public async Task Setting_the_status_of_an_unknown_receiver_reports_no_match()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);

        (await Receivers().SetStatusAsync(Guid.NewGuid(), ReceiverStatus.Registered, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task The_database_refuses_a_status_outside_the_enum()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);
        var receiverRef = Guid.NewGuid();
        await Receivers().AddAsync(new ReceiverReadModel(receiverRef, "Integration Hospital", "Kharkiv oblast"), CancellationToken.None);

        try
        {
            var refused = async () => await ExecuteAsync(
                "UPDATE dbo.Receiver SET [Status] = 9 WHERE ReceiverRef = @r", ("@r", receiverRef));

            await refused.Should().ThrowAsync<SqlException>();
        }
        finally
        {
            await RemoveReceiverAsync(receiverRef);
        }
    }

    [Fact]
    public async Task A_vehicle_names_a_handover_receiver_and_the_truck_list_reads_it_back()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);
        var receiverRef = Guid.NewGuid();
        var vin = "IT" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();
        var start = new DateTime(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);
        var convoys = new ConvoyRepository(ConnectionFactory(), Unattributed);
        var truckList = new ConvoyVehicleRepository(ConnectionFactory(), Unattributed);
        var convoyId = 0;

        try
        {
            await Receivers().AddAsync(new ReceiverReadModel(receiverRef, "Integration Hospital", "Kharkiv oblast"), CancellationToken.None);
            await ExecuteAsync(
                "INSERT INTO dbo.Vehicle (Vin, Plate, [Year], WeightKg, InspectionStatus) VALUES (@vin, 'IT12ABC', 2015, 1800, 2)",
                ("@vin", vin));
            convoyId = await convoys.AddAsync(start, start.AddDays(4), CancellationToken.None);
            await truckList.AddAsync(convoyId, vin, CancellationToken.None);

            (await truckList.GetAsync(convoyId, vin, CancellationToken.None))!.HandoverReceiverRef.Should().BeNull();

            (await truckList.SetHandoverReceiverAsync(convoyId, vin, receiverRef, CancellationToken.None)).Should().BeTrue();

            (await truckList.GetAsync(convoyId, vin, CancellationToken.None))!.HandoverReceiverRef.Should().Be(receiverRef);
            (await truckList.ListAsync(convoyId, CancellationToken.None)).Single().HandoverReceiverRef.Should().Be(receiverRef);
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.ConvoyVehicle WHERE ConvoyId = @c", ("@c", convoyId));
            await ExecuteAsync("DELETE FROM dbo.Convoy WHERE Id = @c", ("@c", convoyId));
            await ExecuteAsync("DELETE FROM dbo.Vehicle WHERE Vin = @vin", ("@vin", vin));
            await RemoveReceiverAsync(receiverRef);
        }
    }

    [Fact]
    public async Task A_withdrawn_vehicle_cannot_be_given_a_handover_receiver_and_an_unlisted_one_matches_nothing()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);
        var receiverRef = Guid.NewGuid();
        var vin = "IT" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();
        var start = new DateTime(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);
        var convoys = new ConvoyRepository(ConnectionFactory(), Unattributed);
        var truckList = new ConvoyVehicleRepository(ConnectionFactory(), Unattributed);
        var convoyId = 0;

        try
        {
            await Receivers().AddAsync(new ReceiverReadModel(receiverRef, "Integration Hospital", "Kharkiv oblast"), CancellationToken.None);
            await ExecuteAsync(
                "INSERT INTO dbo.Vehicle (Vin, Plate, [Year], WeightKg, InspectionStatus) VALUES (@vin, 'IT12ABC', 2015, 1800, 2)",
                ("@vin", vin));
            convoyId = await convoys.AddAsync(start, start.AddDays(4), CancellationToken.None);
            await truckList.AddAsync(convoyId, vin, CancellationToken.None);
            await truckList.WithdrawAsync(convoyId, vin, "Broke down", DateTime.UtcNow, CancellationToken.None);

            (await truckList.SetHandoverReceiverAsync(convoyId, vin, receiverRef, CancellationToken.None)).Should().BeFalse();
            (await truckList.SetHandoverReceiverAsync(convoyId, "NOSUCHVIN", receiverRef, CancellationToken.None)).Should().BeFalse();
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.ConvoyVehicle WHERE ConvoyId = @c", ("@c", convoyId));
            await ExecuteAsync("DELETE FROM dbo.Convoy WHERE Id = @c", ("@c", convoyId));
            await ExecuteAsync("DELETE FROM dbo.Vehicle WHERE Vin = @vin", ("@vin", vin));
            await RemoveReceiverAsync(receiverRef);
        }
    }

    [Fact]
    public async Task The_usage_of_a_receiver_lists_its_boxes_and_the_live_convoys_that_touch_it()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);
        var receiverRef = Guid.NewGuid();
        var vin = "IT" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();
        var manifestId = "IT" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var start = new DateTime(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);
        var convoys = new ConvoyRepository(ConnectionFactory(), Unattributed);
        var boxes = new BoxRepository(ConnectionFactory(), Unattributed);
        var convoyId = 0;
        var arrivedConvoyId = 0;
        var boxId = 0;

        try
        {
            await Receivers().AddAsync(new ReceiverReadModel(receiverRef, "Integration Hospital", "Kharkiv oblast"), CancellationToken.None);
            await ExecuteAsync(
                "INSERT INTO dbo.Vehicle (Vin, Plate, [Year], WeightKg, InspectionStatus) VALUES (@vin, 'IT12ABC', 2015, 1800, 2)",
                ("@vin", vin));
            convoyId = await convoys.AddAsync(start, start.AddDays(4), CancellationToken.None);
            arrivedConvoyId = await convoys.AddAsync(start.AddDays(-30), start.AddDays(-26), CancellationToken.None);
            await ExecuteAsync("UPDATE dbo.Convoy SET ArrivedAt = '2026-08-01' WHERE Id = @c", ("@c", arrivedConvoyId));
            await ExecuteAsync("INSERT INTO dbo.ConvoyVehicle (ConvoyId, Vin) VALUES (@c, @vin)", ("@c", convoyId), ("@vin", vin));
            await ExecuteAsync(
                "INSERT INTO dbo.ConvoyVehicle (ConvoyId, Vin, HandoverReceiverRef) VALUES (@a, @vin, @r)",
                ("@a", arrivedConvoyId), ("@vin", vin), ("@r", receiverRef));

            boxId = await boxes.AddAsync(
                new BoxReadModel(0, 0, null, null, null, receiverRef, null, null, null), CancellationToken.None);
            await ExecuteAsync(
                "INSERT INTO dbo.Manifest (Id, ConvoyId, Vin) VALUES (@m, @c, @vin); INSERT INTO dbo.ManifestBox (BoxId, ManifestId) VALUES (@b, @m)",
                ("@m", manifestId), ("@c", convoyId), ("@vin", vin), ("@b", boxId));

            var usage = await Receivers().GetUsageAsync(receiverRef, CancellationToken.None);

            usage.BoxIds.Should().Equal(boxId);
            usage.ConvoyIds.Should().Equal(convoyId);
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.Manifest WHERE Id = @m", ("@m", manifestId));
            await ExecuteAsync("DELETE FROM dbo.Box WHERE Id = @b", ("@b", boxId));
            await ExecuteAsync("DELETE FROM dbo.ConvoyVehicle WHERE ConvoyId IN (@c, @a)", ("@c", convoyId), ("@a", arrivedConvoyId));
            await ExecuteAsync("DELETE FROM dbo.Convoy WHERE Id IN (@c, @a)", ("@c", convoyId), ("@a", arrivedConvoyId));
            await ExecuteAsync("DELETE FROM dbo.Vehicle WHERE Vin = @vin", ("@vin", vin));
            await RemoveReceiverAsync(receiverRef);
        }
    }

    [Fact]
    public async Task A_receiver_nothing_names_has_empty_usage()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);

        var usage = await Receivers().GetUsageAsync(Guid.NewGuid(), CancellationToken.None);

        usage.BoxIds.Should().BeEmpty();
        usage.ConvoyIds.Should().BeEmpty();
    }

    [Fact]
    public async Task A_hub_is_a_flag_on_a_location_that_round_trips()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);
        var locations = new LocationRepository(ConnectionFactory(), Unattributed);
        var id = 0;

        try
        {
            id = await locations.AddAsync(
                new LocationReadModel(0, "Integration Hub", null, null, null, null, null, IsRegisteredHub: true), CancellationToken.None);

            (await locations.GetByIdAsync(id, CancellationToken.None))!.IsRegisteredHub.Should().BeTrue();

            await locations.UpdateAsync(
                new LocationReadModel(id, "Integration Hub", null, null, null, null, null, IsRegisteredHub: false), CancellationToken.None);

            (await locations.GetByIdAsync(id, CancellationToken.None))!.IsRegisteredHub.Should().BeFalse();
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.Location WHERE Id = @id", ("@id", id));
        }
    }
}

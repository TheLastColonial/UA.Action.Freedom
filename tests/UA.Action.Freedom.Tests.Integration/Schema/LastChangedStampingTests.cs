using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Categories;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Locations;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Application.Vehicles;
using UA.Action.Freedom.Data;
using UA.Action.Freedom.Data.Boxes;
using UA.Action.Freedom.Data.Categories;
using UA.Action.Freedom.Data.Convoys;
using UA.Action.Freedom.Data.Locations;
using UA.Action.Freedom.Data.Manifests;
using UA.Action.Freedom.Data.People;
using UA.Action.Freedom.Data.Receivers;
using UA.Action.Freedom.Data.Vehicles;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Schema;

/// <summary>
/// ADR 0017 against real rows: every insert and every update a repository makes writes the caller
/// into <c>LastChangedBy</c> and the time into <c>LastChangedAt</c>, in the same statement. Each test
/// has one volunteer make a row and another change it, so a statement that stamps on insert but not
/// on update (or the reverse) is caught, and so is one that stamps the wrong person.
/// </summary>
[Trait("Category", "Integration")]
public class LastChangedStampingTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static async Task SkipUnlessProvisionedAsync(CancellationToken cancellationToken) =>
        await SkipUnlessReachableAsync(
            "SELECT TOP 1 LastChangedBy, LastChangedAt FROM dbo.Vehicle; SELECT TOP 1 LastChangedBy FROM dbo.ConvoyVehicleCrew; SELECT TOP 1 1 FROM dbo.PersonDisplay",
            cancellationToken);

    private static async Task<(Guid? By, DateTime? At)> StampOfAsync(string table, string where, params (string, object)[] key)
    {
        var by = await ValueAsync($"SELECT LastChangedBy FROM {table} WHERE {where}", key);
        var at = await ValueAsync($"SELECT LastChangedAt FROM {table} WHERE {where}", key);
        return (by is Guid guid ? guid : null, at is DateTime time ? time : null);
    }

    private static async Task ShouldBeStampedAsync(Guid person, string table, string where, params (string, object)[] key)
    {
        var (by, at) = await StampOfAsync(table, where, key);
        by.Should().Be(person, $"{table} should record who changed it");
        at.Should().NotBeNull($"{table} should record when it was changed");
    }

    private static string NewVin() => "IT" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();

    private static SqlConnectionFactory Connections() => ConnectionFactory();

    [Fact]
    public async Task A_vehicle_records_who_added_it_and_who_last_edited_it_and_who_inspected_it()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);
        var (author, editor) = (await AddVolunteerAsync("Stamp", "Author"), await AddVolunteerAsync("Stamp", "Editor"));
        var vin = NewVin();
        var vehicle = new VehicleReadModel(
            vin, "IT12ABC", "Ford", "Transit", null, TransmissionType.Manual, null, null, false, 2015,
            FuelType.Diesel, null, null, null, 1_800, null, null, null, null);

        try
        {
            await new VehicleRepository(Connections(), AttributedTo(author)).AddAsync(vehicle, Cancel);
            await ShouldBeStampedAsync(author, "dbo.Vehicle", "Vin = @vin", ("@vin", vin));

            await new VehicleRepository(Connections(), AttributedTo(editor)).UpdateAsync(vehicle with { Plate = "IT99ZZZ" }, Cancel);
            await ShouldBeStampedAsync(editor, "dbo.Vehicle", "Vin = @vin", ("@vin", vin));

            await new VehicleRepository(Connections(), AttributedTo(author))
                .RecordInspectionAsync(vin, InspectionStatus.Passed, null, Cancel);
            await ShouldBeStampedAsync(author, "dbo.Vehicle", "Vin = @vin", ("@vin", vin));
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.Vehicle WHERE Vin = @vin", ("@vin", vin));
            await ExecuteAsync("DELETE FROM dbo.Person WHERE Id IN (@a, @b)", ("@a", author), ("@b", editor));
        }
    }

    [Fact]
    public async Task A_volunteers_record_is_stamped_by_whoever_edited_it_and_whoever_linked_the_login()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);
        var (author, editor) = (await AddVolunteerAsync("Stamp", "Author"), await AddVolunteerAsync("Stamp", "Editor"));
        var id = Guid.NewGuid();
        var person = new PersonReadModel(
            id, "Integration", "Subject", new DateTime(1988, 4, 12), new DateTime(2024, 2, 24), null, false, false);

        try
        {
            await new PersonRepository(Connections(), AttributedTo(author)).AddAsync(person, Cancel);
            await ShouldBeStampedAsync(author, "dbo.PersonDetail", "PersonId = @id", ("@id", id));

            await new PersonRepository(Connections(), AttributedTo(editor)).UpdateAsync(person with { Phone = "+447700900123" }, Cancel);
            await ShouldBeStampedAsync(editor, "dbo.PersonDetail", "PersonId = @id", ("@id", id));

            await new PersonRepository(Connections(), AttributedTo(author)).LinkLoginAsync(id, "stamp-" + id, Cancel);
            await ShouldBeStampedAsync(author, "dbo.PersonDetail", "PersonId = @id", ("@id", id));
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.Person WHERE Id IN (@id, @a, @b)", ("@id", id), ("@a", author), ("@b", editor));
        }
    }

    [Fact]
    public async Task A_volunteer_made_by_an_unlinked_login_has_no_author_but_still_a_time()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);
        var id = Guid.NewGuid();
        var person = new PersonReadModel(
            id, "Integration", "Bootstrap", new DateTime(1988, 4, 12), new DateTime(2024, 2, 24), null, false, false);

        try
        {
            await new PersonRepository(Connections(), Unattributed).AddAsync(person, Cancel);

            var (by, at) = await StampOfAsync("dbo.PersonDetail", "PersonId = @id", ("@id", id));
            by.Should().BeNull("no person is invented for a login nobody has linked");
            at.Should().NotBeNull();
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.Person WHERE Id = @id", ("@id", id));
        }
    }

    [Fact]
    public async Task A_receiver_and_its_delivery_detail_record_who_last_changed_them()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);
        var (author, editor) = (await AddVolunteerAsync("Stamp", "Author"), await AddVolunteerAsync("Stamp", "Editor"));
        var receiverRef = Guid.NewGuid();

        try
        {
            await new ReceiverRepository(Connections(), AttributedTo(author))
                .AddAsync(new ReceiverReadModel(receiverRef, "Integration Hospital", "Kharkiv oblast"), Cancel);
            await ShouldBeStampedAsync(author, "dbo.Receiver", "ReceiverRef = @r", ("@r", receiverRef));

            await new ReceiverRepository(Connections(), AttributedTo(editor))
                .UpdateAsync(new ReceiverReadModel(receiverRef, "Integration Hospital No. 2", "Kharkiv oblast"), Cancel);
            await ShouldBeStampedAsync(editor, "dbo.Receiver", "ReceiverRef = @r", ("@r", receiverRef));

            var detail = new ReceiverDetailReadModel(receiverRef, "Contact", "+380000000", "1 Test Street", null, "Kharkiv", null, null);
            var sensitive = new ReceiverDetailRepository(SensitiveConnections(), AttributedTo(author));
            await sensitive.UpsertAsync(detail, Cancel);
            await new ReceiverDetailRepository(SensitiveConnections(), AttributedTo(editor))
                .UpsertAsync(detail with { City = "Lviv" }, Cancel);

            // freedom_app is denied the sensitive schema, so the stamp is read back as the Ground Officer.
            var stamped = await SensitiveValueAsync(
                "SELECT LastChangedBy FROM sensitive.ReceiverDetail WHERE ReceiverRef = @r", receiverRef);
            stamped.Should().Be(editor);
        }
        finally
        {
            await SensitiveExecuteAsync("DELETE FROM sensitive.ReceiverDetail WHERE ReceiverRef = @r", receiverRef);
            await ExecuteAsync("DELETE FROM dbo.Receiver WHERE ReceiverRef = @r", ("@r", receiverRef));
            await ExecuteAsync("DELETE FROM dbo.Person WHERE Id IN (@a, @b)", ("@a", author), ("@b", editor));
        }
    }

    [Fact]
    public async Task A_box_its_items_and_its_label_record_who_last_changed_them()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);
        var (author, editor) = (await AddVolunteerAsync("Stamp", "Author"), await AddVolunteerAsync("Stamp", "Editor"));
        var itemId = Guid.NewGuid();
        var token = Guid.NewGuid();
        var boxes = new BoxRepository(Connections(), AttributedTo(author));
        var edits = new BoxRepository(Connections(), AttributedTo(editor));
        var newBox = new BoxReadModel(0, 0, null, null, null, null, null, null, null);
        int id = 0;
        var category = await AddCategoryAsync();

        try
        {
            id = await boxes.AddAsync(newBox, Cancel);
            await ShouldBeStampedAsync(author, "dbo.Box", "Id = @id", ("@id", id));

            await edits.UpdateAsync(newBox with { Id = id }, Cancel);
            await ShouldBeStampedAsync(editor, "dbo.Box", "Id = @id", ("@id", id));

            await boxes.AddItemAsync(id, new BoxItemReadModel(itemId, "Bandages", new Dictionary<string, string>(), category), Cancel);
            await ShouldBeStampedAsync(author, "dbo.BoxItem", "Id = @id", ("@id", itemId));

            await boxes.IssueQrCodeAsync(id, token, DateTime.UtcNow, Cancel);
            await ShouldBeStampedAsync(author, "dbo.BoxQrCode", "Token = @t", ("@t", token));

            await edits.RevokeActiveQrCodeAsync(id, Cancel);
            await ShouldBeStampedAsync(editor, "dbo.BoxQrCode", "Token = @t", ("@t", token));

            await boxes.ValidateAsync(id, editor, 12, null, null, null, DateTime.UtcNow, Cancel);
            await ShouldBeStampedAsync(author, "dbo.Box", "Id = @id", ("@id", id));
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.Box WHERE Id = @id", ("@id", id));
            await RemoveCategoryAsync(category);
            await ExecuteAsync("DELETE FROM dbo.Person WHERE Id IN (@a, @b)", ("@a", author), ("@b", editor));
        }
    }

    [Fact]
    public async Task A_category_and_its_customs_code_record_who_last_changed_them()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);
        var (author, editor) = (await AddVolunteerAsync("Stamp", "Author"), await AddVolunteerAsync("Stamp", "Editor"));
        int id = 0;

        try
        {
            id = (await new ItemCategoryRepository(Connections(), AttributedTo(author)).AddAsync(
                new ItemCategoryReadModel(0, $"Stamp {Guid.NewGuid():N}", "", false, null, false, false, null), Cancel))!.Value;
            await ShouldBeStampedAsync(author, "dbo.ItemCategory", "Id = @id", ("@id", id));

            await new ItemCategoryRepository(Connections(), AttributedTo(editor))
                .SetCodeAsync(id, CustomsAuthority.EU, "300490", Cancel);
            await ShouldBeStampedAsync(editor, "dbo.CategoryCustomsCode", "CategoryId = @id", ("@id", id));
            await ShouldBeStampedAsync(editor, "dbo.ItemCategory", "Id = @id", ("@id", id));
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.ItemCategory WHERE Id = @id", ("@id", id));
            await ExecuteAsync("DELETE FROM dbo.Person WHERE Id IN (@a, @b)", ("@a", author), ("@b", editor));
        }
    }

    [Fact]
    public async Task A_location_and_its_bay_record_who_last_changed_them()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);
        var (author, editor) = (await AddVolunteerAsync("Stamp", "Author"), await AddVolunteerAsync("Stamp", "Editor"));
        var locations = new LocationRepository(Connections(), AttributedTo(author));
        var bays = new BayRepository(Connections(), AttributedTo(author));
        int locationId = 0;

        try
        {
            locationId = await locations.AddAsync(new LocationReadModel(0, "Integration Depot", null, null, null, null, null), Cancel);
            await ShouldBeStampedAsync(author, "dbo.Location", "Id = @id", ("@id", locationId));

            await new LocationRepository(Connections(), AttributedTo(editor))
                .UpdateAsync(new LocationReadModel(locationId, "Integration Depot 2", null, null, null, null, null), Cancel);
            await ShouldBeStampedAsync(editor, "dbo.Location", "Id = @id", ("@id", locationId));

            var bayId = await bays.AddAsync(new BayReadModel(0, locationId, "A1"), Cancel);
            await ShouldBeStampedAsync(author, "dbo.Bay", "Id = @id", ("@id", bayId));

            await new BayRepository(Connections(), AttributedTo(editor)).UpdateAsync(new BayReadModel(bayId, locationId, "A2"), Cancel);
            await ShouldBeStampedAsync(editor, "dbo.Bay", "Id = @id", ("@id", bayId));
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.Location WHERE Id = @id", ("@id", locationId));
            await ExecuteAsync("DELETE FROM dbo.Person WHERE Id IN (@a, @b)", ("@a", author), ("@b", editor));
        }
    }

    [Fact]
    public async Task A_convoy_its_route_its_truck_list_and_its_crew_record_who_last_changed_them()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);
        var (author, editor) = (await AddVolunteerAsync("Stamp", "Author"), await AddVolunteerAsync("Stamp", "Editor"));
        var vin = NewVin();
        var start = new DateTime(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);
        var convoys = new ConvoyRepository(Connections(), AttributedTo(author));
        var truckList = new ConvoyVehicleRepository(Connections(), AttributedTo(author));
        int convoyId = 0;

        try
        {
            await ExecuteAsync(
                "INSERT INTO dbo.Vehicle (Vin, Plate, [Year], WeightKg, InspectionStatus) VALUES (@vin, 'IT12ABC', 2015, 1800, 2)",
                ("@vin", vin));

            convoyId = await convoys.AddAsync(start, start.AddDays(4), Cancel);
            await ShouldBeStampedAsync(author, "dbo.Convoy", "Id = @id", ("@id", convoyId));

            await new ConvoyRepository(Connections(), AttributedTo(editor))
                .UpdateAsync(new ConvoyReadModel(convoyId, start, start.AddDays(5), null), Cancel);
            await ShouldBeStampedAsync(editor, "dbo.Convoy", "Id = @id", ("@id", convoyId));

            await new ConvoyRepository(Connections(), AttributedTo(editor)).ReplaceRouteAsync(
                convoyId, [new RouteStopReadModel(1, null, null, "Coventry", "United Kingdom", "CV1 1AA", "GB")], Cancel);
            await ShouldBeStampedAsync(editor, "dbo.ConvoyRouteStop", "ConvoyId = @id", ("@id", convoyId));

            await truckList.AddAsync(convoyId, vin, Cancel);
            await ShouldBeStampedAsync(author, "dbo.ConvoyVehicle", "ConvoyId = @id AND Vin = @vin", ("@id", convoyId), ("@vin", vin));

            await new ConvoyVehicleRepository(Connections(), AttributedTo(editor))
                .AssignCrewAsync(convoyId, vin, author, CrewRole.Driver, Cancel);
            await ShouldBeStampedAsync(editor, "dbo.ConvoyVehicleCrew", "ConvoyId = @id AND Vin = @vin", ("@id", convoyId), ("@vin", vin));

            await new ConvoyRepository(Connections(), AttributedTo(editor)).PublishTruckListAsync(convoyId, DateTime.UtcNow, Cancel);
            await ShouldBeStampedAsync(editor, "dbo.Convoy", "Id = @id", ("@id", convoyId));

            await new ConvoyVehicleRepository(Connections(), AttributedTo(author))
                .WithdrawAsync(convoyId, vin, "Broke down", DateTime.UtcNow, Cancel);
            await ShouldBeStampedAsync(author, "dbo.ConvoyVehicle", "ConvoyId = @id AND Vin = @vin", ("@id", convoyId), ("@vin", vin));
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.ConvoyVehicle WHERE ConvoyId = @id", ("@id", convoyId));
            await ExecuteAsync("DELETE FROM dbo.Convoy WHERE Id = @id", ("@id", convoyId));
            await ExecuteAsync("DELETE FROM dbo.Vehicle WHERE Vin = @vin", ("@vin", vin));
            await ExecuteAsync("DELETE FROM dbo.Person WHERE Id IN (@a, @b)", ("@a", author), ("@b", editor));
        }
    }

    [Fact]
    public async Task A_manifest_records_who_opened_it_who_edited_it_and_who_moved_it_on()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);
        var (author, editor) = (await AddVolunteerAsync("Stamp", "Author"), await AddVolunteerAsync("Stamp", "Editor"));
        var vin = NewVin();
        var id = "IT" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        int convoyId = 0;

        try
        {
            await ExecuteAsync(
                "INSERT INTO dbo.Vehicle (Vin, Plate, [Year], WeightKg, InspectionStatus) VALUES (@vin, 'IT12ABC', 2015, 1800, 2)",
                ("@vin", vin));
            convoyId = Convert.ToInt32(await ValueAsync(
                """
                INSERT INTO dbo.Convoy (Start, ExpectedEnd) VALUES ('2026-09-01T06:00:00', '2026-09-05T18:00:00');
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """));
            await ExecuteAsync("INSERT INTO dbo.ConvoyVehicle (ConvoyId, Vin) VALUES (@c, @vin)", ("@c", convoyId), ("@vin", vin));

            var manifests = new ManifestRepository(Connections(), AttributedTo(author));
            var manifest = new ManifestReadModel(id, convoyId, vin, ManifestStatus.Created, null, null);

            await manifests.AddAsync(manifest, Cancel);
            await ShouldBeStampedAsync(author, "dbo.Manifest", "Id = @id", ("@id", id));

            await new ManifestRepository(Connections(), AttributedTo(editor))
                .UpdateAsync(manifest with { DeliveryNotes = "Leave at the gate" }, Cancel);
            await ShouldBeStampedAsync(editor, "dbo.Manifest", "Id = @id", ("@id", id));

            await manifests.TransitionAsync(id, ManifestStatus.Created, ManifestStatus.Proposed, Cancel);
            await ShouldBeStampedAsync(author, "dbo.Manifest", "Id = @id", ("@id", id));
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.Manifest WHERE Id = @id", ("@id", id));
            await ExecuteAsync("DELETE FROM dbo.ConvoyVehicle WHERE ConvoyId = @c", ("@c", convoyId));
            await ExecuteAsync("DELETE FROM dbo.Convoy WHERE Id = @c", ("@c", convoyId));
            await ExecuteAsync("DELETE FROM dbo.Vehicle WHERE Vin = @vin", ("@vin", vin));
            await ExecuteAsync("DELETE FROM dbo.Person WHERE Id IN (@a, @b)", ("@a", author), ("@b", editor));
        }
    }

    [Fact]
    public async Task A_volunteer_who_is_still_on_file_is_shown_by_name_and_an_erased_one_as_a_former_volunteer()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);
        var person = await AddVolunteerAsync("Olena", "Shevchenko");
        var vin = NewVin();
        var vehicle = new VehicleReadModel(
            vin, "IT12ABC", null, null, null, TransmissionType.Manual, null, null, false, 2015,
            FuelType.Diesel, null, null, null, 1_800, null, null, null, null);
        var repository = new VehicleRepository(Connections(), AttributedTo(person));

        try
        {
            await repository.AddAsync(vehicle, Cancel);

            (await repository.GetByVinAsync(vin, Cancel))!.LastChangedByName.Should().Be("Olena Shevchenko");

            // Erasure: the personal data goes, the identity stays because the vehicle row still names it.
            await new PersonRepository(Connections(), Unattributed).DeleteAsync(person, Cancel);

            var erased = await repository.GetByVinAsync(vin, Cancel);
            erased!.LastChangedByName.Should().Be("Former volunteer");
            erased.LastChangedAt.Should().NotBeNull();
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.Vehicle WHERE Vin = @vin", ("@vin", vin));
            await ExecuteAsync("DELETE FROM dbo.Person WHERE Id = @id", ("@id", person));
        }
    }

    [Fact]
    public async Task The_person_display_view_is_the_one_rule_for_a_name_that_may_have_been_erased()
    {
        await SkipUnlessProvisionedAsync(TestContext.Current.CancellationToken);
        var person = await AddVolunteerAsync("Olena", "Shevchenko");

        try
        {
            (await ValueAsync("SELECT DisplayName FROM dbo.PersonDisplay WHERE PersonId = @id", ("@id", person)))
                .Should().Be("Olena Shevchenko");

            await ExecuteAsync("DELETE FROM dbo.PersonDetail WHERE PersonId = @id", ("@id", person));

            (await ValueAsync("SELECT DisplayName FROM dbo.PersonDisplay WHERE PersonId = @id", ("@id", person)))
                .Should().Be("Former volunteer");
            (await ValueAsync("SELECT FirstName + ' ' + LastName FROM dbo.PersonDisplay WHERE PersonId = @id", ("@id", person)))
                .Should().Be("Former volunteer");
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.Person WHERE Id = @id", ("@id", person));
        }
    }

    private static SensitiveSqlConnectionFactory SensitiveConnections() =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Freedom"] = ConnectionString,
                ["ConnectionStrings:FreedomSensitive"] = SensitiveConnectionString,
            })
            .Build());

    private static async Task<object?> SensitiveValueAsync(string sql, Guid receiverRef)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(SensitiveConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@r", receiverRef);
        return await command.ExecuteScalarAsync();
    }

    private static async Task SensitiveExecuteAsync(string sql, Guid receiverRef)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(SensitiveConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@r", receiverRef);
        await command.ExecuteNonQueryAsync();
    }
}

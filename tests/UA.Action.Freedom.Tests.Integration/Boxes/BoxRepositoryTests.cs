using AwesomeAssertions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Data.Boxes;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Boxes;

/// <summary>
/// The Dapper <see cref="BoxRepository"/> against real <c>dbo.Box</c> and <c>dbo.BoxItem</c>.
/// Skips itself when the local stack is not up.
/// </summary>
/// <remarks>
/// Two things need a real database: the conditional validate, which is what makes two Loaders
/// checking the same box at once resolve to one signature rather than the last one to write; and
/// the JSON round trip for an item's open-ended properties, which is the one place in the
/// codebase Dapper's constructor mapping does not carry a read model on its own.
/// </remarks>
[Trait("Category", "Integration")]
public class BoxRepositoryTests
{
    private static async Task<BoxRepository> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync("SELECT COUNT(1) FROM dbo.Box; SELECT COUNT(1) FROM dbo.BoxItem;", cancellationToken);
        return new BoxRepository(ConnectionFactory(), Unattributed);
    }

    private static BoxReadModel ANewBox() => new(
        Id: 0, WeightKg: 0, WidthCm: null, DepthCm: null, HeightCm: null, ReceiverRef: null,
        LocationId: null, ValidatedByPersonId: null, ValidatedAt: null);

    private static Task<Guid> AddVolunteerAsync() => SqlTestDatabase.AddVolunteerAsync("Integration", "Loader", isDriver: false);

    private static Task RemoveVolunteerAsync(Guid id) =>
        ExecuteAsync("DELETE FROM dbo.Person WHERE Id = @id", ("@id", id));

    /// <summary>A location to point a box at, since LocationId is a real foreign key.</summary>
    private static Task<int> AddLocationAsync() => ScalarAsync(
        "INSERT INTO dbo.Location (Name) VALUES ('Integration Depot'); SELECT CAST(SCOPE_IDENTITY() AS int);");

    private static Task RemoveLocationAsync(int id) =>
        ExecuteAsync("DELETE FROM dbo.Location WHERE Id = @id", ("@id", id));

    private static Task RemoveBoxAsync(int id) =>
        ExecuteAsync("DELETE FROM dbo.Box WHERE Id = @id", ("@id", id));

    [Fact]
    public async Task Round_trips_a_box_and_hands_back_the_identifier_it_assigned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);

        var id = await repository.AddAsync(ANewBox(), cancellationToken);

        try
        {
            id.Should().BeGreaterThan(0);

            var stored = await repository.GetByIdAsync(id, cancellationToken);

            stored.Should().Be(ANewBox() with { Id = id, LastChangedAt = stored!.LastChangedAt });
            stored!.Validated.Should().BeFalse();
        }
        finally
        {
            await RemoveBoxAsync(id);
        }
    }

    [Fact]
    public async Task Validates_once_and_refuses_the_second_attempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var loader = await AddVolunteerAsync();
        var id = await repository.AddAsync(ANewBox(), cancellationToken);

        try
        {
            var validatedAt = new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);

            (await repository.ValidateAsync(id, loader, 24, 40m, 30m, 20m, validatedAt, cancellationToken)).Should().BeTrue();

            // Conditional on ValidatedAt IS NULL, so the database settles the race.
            (await repository.ValidateAsync(id, loader, 99, 1m, 1m, 1m, validatedAt, cancellationToken)).Should().BeFalse();

            var stored = await repository.GetByIdAsync(id, cancellationToken);
            stored!.WeightKg.Should().Be(24);
            stored.WidthCm.Should().Be(40m);
            stored.DepthCm.Should().Be(30m);
            stored.HeightCm.Should().Be(20m);
            stored.ValidatedByPersonId.Should().Be(loader);
            stored.Validated.Should().BeTrue();
        }
        finally
        {
            await RemoveBoxAsync(id);
            await RemoveVolunteerAsync(loader);
        }
    }

    [Fact]
    public async Task An_update_cannot_touch_the_weight_or_the_validation_record()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var loader = await AddVolunteerAsync();
        var location = await AddLocationAsync();
        var id = await repository.AddAsync(ANewBox(), cancellationToken);

        try
        {
            await repository.ValidateAsync(id, loader, 24, 40m, 30m, 20m, DateTime.UtcNow, cancellationToken);

            // Even asked directly, the UPDATE statement has no columns for weight/dimensions.
            // LocationId is an ordinary editable field, so it is included to prove the update
            // still goes through for what it is allowed to touch.
            await repository.UpdateAsync(
                ANewBox() with
                {
                    Id = id, LocationId = location, WeightKg = 999, WidthCm = 1m, DepthCm = 1m, HeightCm = 1m,
                    ValidatedByPersonId = null, ValidatedAt = null,
                },
                cancellationToken);

            var stored = await repository.GetByIdAsync(id, cancellationToken);
            stored!.LocationId.Should().Be(location);
            stored.WeightKg.Should().Be(24);
            stored.WidthCm.Should().Be(40m);
            stored.DepthCm.Should().Be(30m);
            stored.HeightCm.Should().Be(20m);
            stored.ValidatedByPersonId.Should().Be(loader);
        }
        finally
        {
            await RemoveBoxAsync(id);
            await RemoveVolunteerAsync(loader);
            await RemoveLocationAsync(location);
        }
    }

    [Fact]
    public async Task Round_trips_an_items_open_ended_properties_through_JSON()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(ANewBox(), cancellationToken);
        var category = await AddCategoryAsync();

        try
        {
            var item = new BoxItemReadModel(
                Guid.NewGuid(),
                "Blankets",
                new Dictionary<string, string> { ["size"] = "double", ["condition"] = "new" },
                category);

            await repository.AddItemAsync(id, item, cancellationToken);

            var packed = await repository.ListItemsAsync(id, cancellationToken);

            var stored = packed.Should().ContainSingle().Subject;
            stored.Description.Should().Be("Blankets");
            stored.Properties.Should().BeEquivalentTo(item.Properties);
        }
        finally
        {
            await RemoveBoxAsync(id);
            await RemoveCategoryAsync(category);
        }
    }

    [Fact]
    public async Task Round_trips_an_items_category_quantity_value_expiry_and_code()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(ANewBox(), cancellationToken);
        var category = await AddCategoryAsync();

        try
        {
            var item = new BoxItemReadModel(
                Guid.NewGuid(), "Paracetamol", new Dictionary<string, string>(), category,
                CommodityCode: "30049000", Quantity: 40, ValueGbp: 62.50m, ValueSource: ValueSource.Estimate,
                ExpiresOn: new DateOnly(2027, 3, 31));

            await repository.AddItemAsync(id, item, cancellationToken);

            (await repository.ListItemsAsync(id, cancellationToken)).Should().ContainSingle().Which.Should().BeEquivalentTo(item);
        }
        finally
        {
            await RemoveBoxAsync(id);
            await RemoveCategoryAsync(category);
        }
    }

    [Fact]
    public async Task An_item_with_no_value_quantity_expiry_or_code_reads_back_with_them_empty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(ANewBox(), cancellationToken);
        var category = await AddCategoryAsync();

        try
        {
            await repository.AddItemAsync(
                id, new BoxItemReadModel(Guid.NewGuid(), "Bandages", new Dictionary<string, string>(), category),
                cancellationToken);

            var stored = (await repository.ListItemsAsync(id, cancellationToken)).Should().ContainSingle().Subject;
            stored.Quantity.Should().BeNull();
            stored.ValueGbp.Should().BeNull();
            stored.ValueSource.Should().BeNull();
            stored.ExpiresOn.Should().BeNull();
            stored.CommodityCode.Should().BeNull();
        }
        finally
        {
            await RemoveBoxAsync(id);
            await RemoveCategoryAsync(category);
        }
    }

    [Fact]
    public async Task A_value_without_its_source_is_refused_by_the_table()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(ANewBox(), cancellationToken);
        var category = await AddCategoryAsync();

        try
        {
            var act = () => repository.AddItemAsync(
                id,
                new BoxItemReadModel(
                    Guid.NewGuid(), "Bandages", new Dictionary<string, string>(), category, ValueGbp: 5m),
                cancellationToken);

            await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
        }
        finally
        {
            await RemoveBoxAsync(id);
            await RemoveCategoryAsync(category);
        }
    }

    [Fact]
    public async Task Reads_an_item_whose_stored_properties_hold_a_JSON_number()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(ANewBox(), cancellationToken);
        var category = await AddCategoryAsync();

        try
        {
            await ExecuteAsync(
                "INSERT INTO dbo.BoxItem (Id, BoxId, CategoryId, Description, PropertiesJson) VALUES (NEWID(), @id, @category, 'Seeded', '{\"quantity\":10}')",
                ("@id", id),
                ("@category", category));

            var packed = await repository.ListItemsAsync(id, cancellationToken);

            packed.Should().ContainSingle().Which.Properties.Should().ContainKey("quantity");
        }
        finally
        {
            await RemoveBoxAsync(id);
            await RemoveCategoryAsync(category);
        }
    }

    [Fact]
    public async Task An_item_with_no_properties_reads_back_as_an_empty_bag_not_a_null()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(ANewBox(), cancellationToken);
        var category = await AddCategoryAsync();

        try
        {
            await repository.AddItemAsync(
                id, new BoxItemReadModel(Guid.NewGuid(), "Bandages", new Dictionary<string, string>(), category),
                cancellationToken);

            var packed = await repository.ListItemsAsync(id, cancellationToken);

            packed.Should().ContainSingle().Which.Properties.Should().BeEmpty();
        }
        finally
        {
            await RemoveBoxAsync(id);
            await RemoveCategoryAsync(category);
        }
    }

    [Fact]
    public async Task Unpacking_is_scoped_to_the_box_it_was_packed_into()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var first = await repository.AddAsync(ANewBox(), cancellationToken);
        var second = await repository.AddAsync(ANewBox(), cancellationToken);
        var category = await AddCategoryAsync();

        try
        {
            var item = new BoxItemReadModel(Guid.NewGuid(), "Blankets", new Dictionary<string, string>(), category);
            await repository.AddItemAsync(first, item, cancellationToken);

            // Naming the wrong box must not empty it, nor report success.
            (await repository.DeleteItemAsync(second, item.Id, cancellationToken)).Should().BeFalse();
            (await repository.ListItemsAsync(first, cancellationToken)).Should().ContainSingle();

            (await repository.DeleteItemAsync(first, item.Id, cancellationToken)).Should().BeTrue();
            (await repository.ListItemsAsync(first, cancellationToken)).Should().BeEmpty();
        }
        finally
        {
            await RemoveBoxAsync(first);
            await RemoveBoxAsync(second);
            await RemoveCategoryAsync(category);
        }
    }

    [Fact]
    public async Task Deleting_a_box_takes_its_contents_with_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(ANewBox(), cancellationToken);
        var category = await AddCategoryAsync();

        try
        {
            await repository.AddItemAsync(
                id, new BoxItemReadModel(Guid.NewGuid(), "Blankets", new Dictionary<string, string>(), category),
                cancellationToken);

            (await repository.DeleteAsync(id, cancellationToken)).Should().BeTrue();

            // The cascade is what stops unpacked items outliving the box they were in.
            (await repository.ListItemsAsync(id, cancellationToken)).Should().BeEmpty();
        }
        finally
        {
            await RemoveCategoryAsync(category);
        }
    }

    [Fact]
    public async Task A_category_that_still_has_items_cannot_be_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(ANewBox(), cancellationToken);
        var category = await AddCategoryAsync();

        try
        {
            await repository.AddItemAsync(
                id, new BoxItemReadModel(Guid.NewGuid(), "Blankets", new Dictionary<string, string>(), category),
                cancellationToken);

            var act = () => RemoveCategoryAsync(category);

            await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
        }
        finally
        {
            await RemoveBoxAsync(id);
            await RemoveCategoryAsync(category);
        }
    }

    [Fact]
    public async Task An_item_remembers_the_donation_it_came_in()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(ANewBox(), cancellationToken);
        var category = await AddCategoryAsync();
        var donorId = Guid.NewGuid();
        await ExecuteAsync("INSERT INTO dbo.Donor (Id) VALUES (@id)", ("@id", donorId));
        var donationId = await ScalarAsync(
            "INSERT INTO dbo.Donation (DonorId, ReceivedOn) VALUES (@donor, '2026-09-20'); SELECT CAST(SCOPE_IDENTITY() AS int);",
            ("@donor", donorId));

        try
        {
            var item = new BoxItemReadModel(
                Guid.NewGuid(), "Tins", new Dictionary<string, string>(), category, DonationId: donationId);
            await repository.AddItemAsync(id, item, cancellationToken);

            (await repository.ListItemsAsync(id, cancellationToken)).Should().ContainSingle()
                .Which.DonationId.Should().Be(donationId);
        }
        finally
        {
            await RemoveBoxAsync(id);
            await ExecuteAsync("DELETE FROM dbo.Donation WHERE Id = @id; DELETE FROM dbo.Donor WHERE Id = @donor", ("@id", donationId), ("@donor", donorId));
            await RemoveCategoryAsync(category);
        }
    }
}

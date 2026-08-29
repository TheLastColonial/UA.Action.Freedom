using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Data;
using UA.Action.Freedom.Data.Boxes;

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
    private const string DefaultLocalConnectionString =
        "Server=localhost,1433;Database=Freedom;User Id=freedom_app;Password=Local_Freedom_App_1;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Freedom") ?? DefaultLocalConnectionString;

    private static async Task<BoxRepository> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM dbo.Box; SELECT COUNT(1) FROM dbo.BoxItem;";
            await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            Assert.Skip($"Freedom database with dbo.Box is not reachable: {exception.Message}");
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Freedom"] = ConnectionString })
            .Build();

        return new BoxRepository(new SqlConnectionFactory(configuration));
    }

    private static BoxReadModel ANewBox() => new(
        Id: 0, WeightKg: 0, ReceiverRef: null,
        House: "Unit 4", Street: "Cross Road", City: "Coventry", Country: "United Kingdom", Postcode: "CV1 2AB",
        ValidatedByPersonId: null, ValidatedAt: null);

    private static async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A volunteer to act as the Loader, since the validator is a real foreign key.</summary>
    private static async Task<Guid> AddVolunteerAsync()
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT INTO dbo.Person (Id, FirstName, LastName, DateOfBirth, Joined)
            VALUES (@id, 'Integration', 'Loader', '1990-01-01', '2024-01-01')
            """,
            ("@id", id));
        return id;
    }

    private static Task RemoveVolunteerAsync(Guid id) =>
        ExecuteAsync("DELETE FROM dbo.Person WHERE Id = @id", ("@id", id));

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

            stored.Should().Be(ANewBox() with { Id = id });
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

            (await repository.ValidateAsync(id, loader, 24, validatedAt, cancellationToken)).Should().BeTrue();

            // Conditional on ValidatedAt IS NULL, so the database settles the race.
            (await repository.ValidateAsync(id, loader, 99, validatedAt, cancellationToken)).Should().BeFalse();

            var stored = await repository.GetByIdAsync(id, cancellationToken);
            stored!.WeightKg.Should().Be(24);
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
        var id = await repository.AddAsync(ANewBox(), cancellationToken);

        try
        {
            await repository.ValidateAsync(id, loader, 24, DateTime.UtcNow, cancellationToken);

            // Even asked directly, the UPDATE statement has no columns for these.
            await repository.UpdateAsync(
                ANewBox() with { Id = id, City = "Dover", WeightKg = 999, ValidatedByPersonId = null, ValidatedAt = null },
                cancellationToken);

            var stored = await repository.GetByIdAsync(id, cancellationToken);
            stored!.City.Should().Be("Dover");
            stored.WeightKg.Should().Be(24);
            stored.ValidatedByPersonId.Should().Be(loader);
        }
        finally
        {
            await RemoveBoxAsync(id);
            await RemoveVolunteerAsync(loader);
        }
    }

    [Fact]
    public async Task Round_trips_an_items_open_ended_properties_through_JSON()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(ANewBox(), cancellationToken);

        try
        {
            var item = new BoxItemReadModel(
                Guid.NewGuid(),
                "Blankets",
                new Dictionary<string, string> { ["size"] = "double", ["condition"] = "new" });

            await repository.AddItemAsync(id, item, cancellationToken);

            var packed = await repository.ListItemsAsync(id, cancellationToken);

            var stored = packed.Should().ContainSingle().Subject;
            stored.Description.Should().Be("Blankets");
            stored.Properties.Should().BeEquivalentTo(item.Properties);
        }
        finally
        {
            await RemoveBoxAsync(id);
        }
    }

    [Fact]
    public async Task An_item_with_no_properties_reads_back_as_an_empty_bag_not_a_null()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(ANewBox(), cancellationToken);

        try
        {
            await repository.AddItemAsync(
                id, new BoxItemReadModel(Guid.NewGuid(), "Bandages", new Dictionary<string, string>()), cancellationToken);

            var packed = await repository.ListItemsAsync(id, cancellationToken);

            packed.Should().ContainSingle().Which.Properties.Should().BeEmpty();
        }
        finally
        {
            await RemoveBoxAsync(id);
        }
    }

    [Fact]
    public async Task Unpacking_is_scoped_to_the_box_it_was_packed_into()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var first = await repository.AddAsync(ANewBox(), cancellationToken);
        var second = await repository.AddAsync(ANewBox(), cancellationToken);

        try
        {
            var item = new BoxItemReadModel(Guid.NewGuid(), "Blankets", new Dictionary<string, string>());
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
        }
    }

    [Fact]
    public async Task Deleting_a_box_takes_its_contents_with_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(ANewBox(), cancellationToken);

        await repository.AddItemAsync(
            id, new BoxItemReadModel(Guid.NewGuid(), "Blankets", new Dictionary<string, string>()), cancellationToken);

        (await repository.DeleteAsync(id, cancellationToken)).Should().BeTrue();

        // The cascade is what stops unpacked items outliving the box they were in.
        (await repository.ListItemsAsync(id, cancellationToken)).Should().BeEmpty();
    }
}

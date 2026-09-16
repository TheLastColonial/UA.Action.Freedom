using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using UA.Action.Freedom.Application.Locations;
using UA.Action.Freedom.Data;
using UA.Action.Freedom.Data.Locations;

namespace UA.Action.Freedom.Tests.Integration.Locations;

/// <summary>
/// The Dapper <see cref="BayRepository"/> against a real <c>dbo.Bay</c>, including the
/// per-location unique-code constraint. Skips itself when the local stack is not up.
/// </summary>
[Trait("Category", "Integration")]
public class BayRepositoryTests
{
    private const string DefaultLocalConnectionString =
        "Server=localhost,1433;Database=Freedom;User Id=freedom_app;Password=Local_Freedom_App_1;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Freedom") ?? DefaultLocalConnectionString;

    private static async Task<BayRepository> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM dbo.Bay";
            await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            Assert.Skip($"Freedom database with dbo.Bay is not reachable: {exception.Message}");
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Freedom"] = ConnectionString })
            .Build();

        return new BayRepository(new SqlConnectionFactory(configuration));
    }

    private static async Task<int> AddLocationAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO dbo.Location (Name) VALUES ('Integration Depot'); SELECT CAST(SCOPE_IDENTITY() AS int);";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static Task RemoveLocationAsync(int id) =>
        ExecuteAsync("DELETE FROM dbo.Location WHERE Id = @id", ("@id", id));

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

    [Fact]
    public async Task Round_trips_a_bay_and_hands_back_the_identifier_it_assigned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var locationId = await AddLocationAsync();

        try
        {
            var id = await repository.AddAsync(new BayReadModel(0, locationId, "A1"), cancellationToken);

            id.Should().BeGreaterThan(0);
            var stored = await repository.GetByIdAsync(id, cancellationToken);
            stored.Should().Be(new BayReadModel(id, locationId, "A1"));
        }
        finally
        {
            // The location cascade takes its bays with it.
            await RemoveLocationAsync(locationId);
        }
    }

    [Fact]
    public async Task A_second_bay_at_the_same_location_cannot_reuse_a_code()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var locationId = await AddLocationAsync();

        try
        {
            (await repository.CodeExistsAsync(locationId, "A1", null, cancellationToken)).Should().BeFalse();

            await repository.AddAsync(new BayReadModel(0, locationId, "A1"), cancellationToken);

            (await repository.CodeExistsAsync(locationId, "A1", null, cancellationToken)).Should().BeTrue();
        }
        finally
        {
            await RemoveLocationAsync(locationId);
        }
    }

    [Fact]
    public async Task Two_different_locations_may_each_have_a_bay_with_the_same_code()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var first = await AddLocationAsync();
        var second = await AddLocationAsync();

        try
        {
            await repository.AddAsync(new BayReadModel(0, first, "A1"), cancellationToken);

            (await repository.CodeExistsAsync(second, "A1", null, cancellationToken)).Should().BeFalse();
        }
        finally
        {
            await RemoveLocationAsync(first);
            await RemoveLocationAsync(second);
        }
    }

    [Fact]
    public async Task List_returns_bays_ordered_by_code()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var locationId = await AddLocationAsync();

        try
        {
            await repository.AddAsync(new BayReadModel(0, locationId, "B1"), cancellationToken);
            await repository.AddAsync(new BayReadModel(0, locationId, "A1"), cancellationToken);

            var bays = await repository.ListByLocationAsync(locationId, cancellationToken);

            bays.Select(bay => bay.Code).Should().Equal("A1", "B1");
        }
        finally
        {
            await RemoveLocationAsync(locationId);
        }
    }

    [Fact]
    public async Task Deleting_a_location_takes_its_bays_with_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var locationId = await AddLocationAsync();
        var bayId = await repository.AddAsync(new BayReadModel(0, locationId, "A1"), cancellationToken);

        await RemoveLocationAsync(locationId);

        (await repository.GetByIdAsync(bayId, cancellationToken)).Should().BeNull();
    }
}

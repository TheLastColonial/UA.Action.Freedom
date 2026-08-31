using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Data;
using UA.Action.Freedom.Data.Convoys;

namespace UA.Action.Freedom.Tests.Integration.Convoys;

/// <summary>
/// The Dapper <see cref="ConvoyRepository"/> against real <c>dbo.Convoy</c>,
/// <c>dbo.ConvoyRouteStop</c> and <c>dbo.Vehicle</c>. Needs the local stack up
/// (<c>iac/local</c> + <c>tofu apply</c>) or a <c>ConnectionStrings__Freedom</c> pointing at an
/// equivalent database; skips itself otherwise.
/// </summary>
/// <remarks>
/// Two things here can only be tested against a real database: the route transaction, and the
/// foreign key that releases a convoy's vehicles instead of deleting them.
/// </remarks>
[Trait("Category", "Integration")]
public class ConvoyRepositoryTests
{
    private const string DefaultLocalConnectionString =
        "Server=localhost,1433;Database=Freedom;User Id=sa;Password=Local_Freedom_Dev_1;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Freedom") ?? DefaultLocalConnectionString;

    private static readonly DateTime Start = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ExpectedEnd = new(2026, 9, 5, 18, 0, 0, DateTimeKind.Utc);

    private static async Task<ConvoyRepository> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM dbo.Convoy; SELECT COUNT(1) FROM dbo.ConvoyRouteStop;";
            await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            Assert.Skip($"Freedom database with dbo.Convoy is not reachable: {exception.Message}");
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Freedom"] = ConnectionString })
            .Build();

        return new ConvoyRepository(new SqlConnectionFactory(configuration));
    }

    private static RouteStopReadModel AStop(int sequence, string city, string postcode) =>
        new(sequence, "Unit 4", "Cross Road", city, "United Kingdom", postcode);

    private static string NewVin() => "IT" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();

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

    private static Task AddVehicleAsync(string vin) => ExecuteAsync(
        "INSERT INTO dbo.Vehicle (Vin, Plate, [Year], WeightKg) VALUES (@vin, 'IT12ABC', 2015, 1800)",
        ("@vin", vin));

    private static Task RemoveVehicleAsync(string vin) =>
        ExecuteAsync("DELETE FROM dbo.Vehicle WHERE Vin = @vin", ("@vin", vin));

    private static Task RemoveConvoyAsync(int id) =>
        ExecuteAsync("DELETE FROM dbo.Convoy WHERE Id = @id", ("@id", id));

    [Fact]
    public async Task Round_trips_a_convoy_and_hands_back_the_identifier_it_assigned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);

        var id = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            id.Should().BeGreaterThan(0);

            var stored = await repository.GetByIdAsync(id, cancellationToken);

            stored.Should().Be(new ConvoyReadModel(id, Start, ExpectedEnd, TruckListPublishedAt: null));
            stored!.TruckListPublished.Should().BeFalse();
        }
        finally
        {
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task Replaces_a_route_whole_and_reads_it_back_in_order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            await repository.ReplaceRouteAsync(
                id, [AStop(1, "Coventry", "CV1 2AB"), AStop(2, "Warszawa", "80-180")], cancellationToken);

            var route = await repository.GetRouteAsync(id, cancellationToken);
            route.Select(stop => stop.City).Should().ContainInOrder("Coventry", "Warszawa");

            // Replacing is a replacement, not an append: the old stops have to be gone.
            await repository.ReplaceRouteAsync(id, [AStop(1, "Dover", "CT16 1JA")], cancellationToken);

            var replaced = await repository.GetRouteAsync(id, cancellationToken);
            replaced.Should().ContainSingle();
            replaced[0].City.Should().Be("Dover");
        }
        finally
        {
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task Deleting_a_convoy_takes_its_route_with_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);

        await repository.ReplaceRouteAsync(id, [AStop(1, "Coventry", "CV1 2AB")], cancellationToken);

        (await repository.DeleteAsync(id, cancellationToken)).Should().BeTrue();

        // The cascade is what stops a cancelled convoy leaving orphan stops behind.
        (await repository.GetRouteAsync(id, cancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task Publishes_a_truck_list_once_and_refuses_the_second_attempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            var published = await repository.PublishTruckListAsync(id, DateTime.UtcNow, cancellationToken);
            published.Should().BeTrue();

            // The UPDATE is conditional on nothing having published yet, so the database — not
            // the application — is what settles a race between two dispatchers.
            var again = await repository.PublishTruckListAsync(id, DateTime.UtcNow, cancellationToken);
            again.Should().BeFalse();

            var stored = await repository.GetByIdAsync(id, cancellationToken);
            stored!.TruckListPublished.Should().BeTrue();
        }
        finally
        {
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task Assigns_and_releases_a_vehicle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();

        try
        {
            await AddVehicleAsync(vin);

            (await repository.AssignVehicleAsync(id, vin, cancellationToken)).Should().BeTrue();

            var onConvoy = await repository.ListVehiclesAsync(id, cancellationToken);
            onConvoy.Should().ContainSingle(vehicle => vehicle.Vin == vin);

            (await repository.UnassignVehicleAsync(id, vin, cancellationToken)).Should().BeTrue();
            (await repository.ListVehiclesAsync(id, cancellationToken)).Should().BeEmpty();

            // Unassigning something that is not on this convoy is a caller mistake, not a no-op.
            (await repository.UnassignVehicleAsync(id, vin, cancellationToken)).Should().BeFalse();

            // And there is no vehicle with this VIN at all.
            (await repository.AssignVehicleAsync(id, "NOSUCHVIN000000", cancellationToken)).Should().BeFalse();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task Cancelling_a_convoy_releases_its_vehicles_rather_than_deleting_them()
    {
        // Vehicles are themselves part of the aid. A cancelled convoy must not take donated
        // vehicles out of the system with it — the foreign key is ON DELETE SET NULL for this.
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();

        try
        {
            await AddVehicleAsync(vin);
            await repository.AssignVehicleAsync(id, vin, cancellationToken);

            await repository.DeleteAsync(id, cancellationToken);

            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT ConvoyId FROM dbo.Vehicle WHERE Vin = @vin";
            command.Parameters.AddWithValue("@vin", vin);

            var convoyId = await command.ExecuteScalarAsync(cancellationToken);

            convoyId.Should().Be(DBNull.Value);
        }
        finally
        {
            await RemoveVehicleAsync(vin);
        }
    }
}

using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using UA.Action.Freedom.Application.Vehicles;
using UA.Action.Freedom.Data;
using UA.Action.Freedom.Data.Vehicles;
using UA.Action.Freedom.Domain;

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
    private const string DefaultLocalConnectionString =
        "Server=localhost,1433;Database=Freedom;User Id=sa;Password=Local_Freedom_Dev_1;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Freedom") ?? DefaultLocalConnectionString;

    private static async Task<VehicleRepository> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM dbo.Vehicle";
            await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            Assert.Skip($"Freedom database with dbo.Vehicle is not reachable: {exception.Message}");
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Freedom"] = ConnectionString })
            .Build();

        return new VehicleRepository(new SqlConnectionFactory(configuration));
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
        WeightKg: 1_800);

    private static string NewVin() => "IT" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();

    private static async Task RemoveAsync(string vin)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM dbo.Vehicle WHERE Vin = @vin";
        command.Parameters.AddWithValue("@vin", vin);
        await command.ExecuteNonQueryAsync();
    }

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
            (await repository.DeleteAsync(vin, cancellationToken)).Should().BeTrue();
            (await repository.ExistsAsync(vin, cancellationToken)).Should().BeFalse();
            (await repository.DeleteAsync(vin, cancellationToken)).Should().BeFalse();
        }
        finally
        {
            await RemoveAsync(vin);
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

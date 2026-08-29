using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Data;
using UA.Action.Freedom.Data.People;

namespace UA.Action.Freedom.Tests.Integration.People;

/// <summary>
/// The Dapper <see cref="PersonRepository"/> against a real <c>dbo.Person</c>. Needs the local
/// stack up (<c>iac/local</c> + <c>tofu apply</c>) or a <c>ConnectionStrings__Freedom</c>
/// pointing at an equivalent database; skips itself otherwise.
/// </summary>
[Trait("Category", "Integration")]
public class PersonRepositoryTests
{
    private const string DefaultLocalConnectionString =
        "Server=localhost,1433;Database=Freedom;User Id=sa;Password=Local_Freedom_Dev_1;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Freedom") ?? DefaultLocalConnectionString;

    private static async Task<PersonRepository> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM dbo.Person";
            await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            Assert.Skip($"Freedom database with dbo.Person is not reachable: {exception.Message}");
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Freedom"] = ConnectionString })
            .Build();

        return new PersonRepository(new SqlConnectionFactory(configuration));
    }

    /// <summary>
    /// A surname nothing else in the database will share, so a paged list can find this row
    /// without asserting on a total count.
    /// </summary>
    private static string NewSurname() => "IT" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    private static PersonReadModel APerson(Guid id, string surname, bool isDriver = false, bool committed = false) => new(
        Id: id,
        FirstName: "Integration",
        LastName: surname,
        DateOfBirth: new DateTime(1988, 4, 12, 0, 0, 0, DateTimeKind.Utc),
        Joined: new DateTime(2024, 2, 24, 0, 0, 0, DateTimeKind.Utc),
        Phone: "+447700900123",
        IsDriver: isDriver,
        Committed: committed);

    private static async Task RemoveAsync(Guid id)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM dbo.Person WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Round_trips_every_field_through_the_database()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        var surname = NewSurname();

        try
        {
            await repository.AddAsync(APerson(id, surname, isDriver: true, committed: true), cancellationToken);

            var stored = await repository.GetByIdAsync(id, cancellationToken);

            // Record equality: proves every column round-trips as the CLR type the read model's
            // constructor expects, which is what catches a bit/int or datetime2 mismatch.
            stored.Should().Be(APerson(id, surname, isDriver: true, committed: true));
        }
        finally
        {
            await RemoveAsync(id);
        }
    }

    [Fact]
    public async Task Reports_whether_an_update_matched_a_row()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        var surname = NewSurname();

        try
        {
            await repository.AddAsync(APerson(id, surname), cancellationToken);

            var changed = await repository.UpdateAsync(
                APerson(id, surname) with { IsDriver = true, Committed = true, Phone = null },
                cancellationToken);

            changed.Should().BeTrue();

            var stored = await repository.GetByIdAsync(id, cancellationToken);
            stored!.IsDriver.Should().BeTrue();
            stored.Committed.Should().BeTrue();
            stored.Phone.Should().BeNull();

            var missed = await repository.UpdateAsync(APerson(Guid.NewGuid(), surname), cancellationToken);
            missed.Should().BeFalse();
        }
        finally
        {
            await RemoveAsync(id);
        }
    }

    [Fact]
    public async Task Exists_and_delete_follow_the_row_through_its_life()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        var surname = NewSurname();

        try
        {
            (await repository.ExistsAsync(id, cancellationToken)).Should().BeFalse();

            await repository.AddAsync(APerson(id, surname), cancellationToken);
            (await repository.ExistsAsync(id, cancellationToken)).Should().BeTrue();

            (await repository.DeleteAsync(id, cancellationToken)).Should().BeTrue();
            (await repository.ExistsAsync(id, cancellationToken)).Should().BeFalse();

            (await repository.DeleteAsync(id, cancellationToken)).Should().BeFalse();
        }
        finally
        {
            await RemoveAsync(id);
        }
    }

    [Fact]
    public async Task Lists_the_volunteer_among_the_others()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        var surname = NewSurname();

        try
        {
            await repository.AddAsync(APerson(id, surname), cancellationToken);

            var page = await repository.ListAsync(1, 200, driversOnly: false, cancellationToken);

            page.Should().ContainSingle(person => person.Id == id);
        }
        finally
        {
            await RemoveAsync(id);
        }
    }

    [Fact]
    public async Task Leaves_non_drivers_out_of_the_drivers_only_page()
    {
        // The dispatcher's shortlist. A non-driver appearing here is someone being asked to
        // drive a convoy leg they never volunteered for.
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var driverId = Guid.NewGuid();
        var packerId = Guid.NewGuid();
        var surname = NewSurname();

        try
        {
            await repository.AddAsync(APerson(driverId, surname, isDriver: true), cancellationToken);
            await repository.AddAsync(APerson(packerId, surname, isDriver: false), cancellationToken);

            var drivers = await repository.ListAsync(1, 200, driversOnly: true, cancellationToken);

            drivers.Should().Contain(person => person.Id == driverId);
            drivers.Should().NotContain(person => person.Id == packerId);
        }
        finally
        {
            await RemoveAsync(driverId);
            await RemoveAsync(packerId);
        }
    }
}

using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Data.Convoys;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.Convoys.ConvoyFixtures;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Convoys;

/// <summary>
/// <see cref="ConvoyLeaderRepository"/> against a real database: the open-assignment rule is a filtered unique
/// index, and the nomination is one transaction that needs the real crew table.
/// </summary>
[Trait("Category", "Integration")]
public class ConvoyLeaderRepositoryTests
{
    private static async Task<(ConvoyRepository Convoys, ConvoyVehicleRepository TruckList, ConvoyLeaderRepository Leaders)> ConnectOrSkipAsync(
        CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync(Probe + "SELECT COUNT(1) FROM dbo.ConvoyLeaderAssignment;", cancellationToken);
        return (
            new ConvoyRepository(ConnectionFactory(), Unattributed),
            new ConvoyVehicleRepository(ConnectionFactory(), Unattributed),
            new ConvoyLeaderRepository(ConnectionFactory(), Unattributed));
    }

    [Fact]
    public async Task Nominating_a_new_leader_closes_the_old_assignment_and_keeps_the_history()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, leaders) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var first = await AddVolunteerAsync("First", "Leader");
        var second = await AddVolunteerAsync("Second", "Leader");

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(id, vin, cancellationToken);
            await truckList.AssignCrewAsync(id, vin, first, CrewRole.Driver, cancellationToken);
            await truckList.AssignCrewAsync(id, vin, second, CrewRole.Driver, cancellationToken);
            var firstAt = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
            var secondAt = firstAt.AddDays(1);

            (await leaders.NominateAsync(id, first, firstAt, cancellationToken)).Should().Be(NominateLeaderResult.Nominated);
            (await leaders.NominateAsync(id, second, secondAt, cancellationToken)).Should().Be(NominateLeaderResult.Nominated);

            var history = await leaders.HistoryAsync(id, cancellationToken);
            history.Select(a => (a.PersonId, a.From, a.Until)).Should().Equal(
                (second, secondAt, (DateTime?)null),
                (first, firstAt, secondAt));
            history[0].PersonName.Should().Be("Second Leader");
            (await leaders.IsCurrentLeaderAsync(id, second, cancellationToken)).Should().BeTrue();
            (await leaders.IsCurrentLeaderAsync(id, first, cancellationToken)).Should().BeFalse();
            (await ScalarAsync(
                "SELECT COUNT(1) FROM dbo.ConvoyLeaderAssignment WHERE ConvoyId = @id AND Until IS NULL", ("@id", id)))
                .Should().Be(1);
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.ConvoyLeaderAssignment WHERE ConvoyId = @id", ("@id", id));
            await RemoveConvoyAsync(id);
            await ExecuteAsync("DELETE FROM dbo.Vehicle WHERE Vin = @vin", ("@vin", vin));
            await ExecuteAsync("DELETE FROM dbo.Person WHERE Id IN (@a, @b)", ("@a", first), ("@b", second));
        }
    }

    [Fact]
    public async Task Refuses_a_passenger_a_stranger_and_the_sitting_leader_and_writes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, leaders) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var driver = await AddVolunteerAsync("Only", "Driver");
        var passenger = await AddVolunteerAsync("Just", "Passenger");
        var stranger = await AddVolunteerAsync("Not", "Crewed");

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(id, vin, cancellationToken);
            await truckList.AssignCrewAsync(id, vin, driver, CrewRole.Driver, cancellationToken);
            await truckList.AssignCrewAsync(id, vin, passenger, CrewRole.Passenger, cancellationToken);

            (await leaders.NominateAsync(id, passenger, DateTime.UtcNow, cancellationToken)).Should().Be(NominateLeaderResult.NotADriverOnConvoy);
            (await leaders.NominateAsync(id, stranger, DateTime.UtcNow, cancellationToken)).Should().Be(NominateLeaderResult.NotADriverOnConvoy);
            (await leaders.HistoryAsync(id, cancellationToken)).Should().BeEmpty();

            await leaders.NominateAsync(id, driver, DateTime.UtcNow, cancellationToken);
            (await leaders.NominateAsync(id, driver, DateTime.UtcNow, cancellationToken)).Should().Be(NominateLeaderResult.AlreadyLeader);
            (await leaders.HistoryAsync(id, cancellationToken)).Should().ContainSingle();
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.ConvoyLeaderAssignment WHERE ConvoyId = @id", ("@id", id));
            await RemoveConvoyAsync(id);
            await ExecuteAsync("DELETE FROM dbo.Vehicle WHERE Vin = @vin", ("@vin", vin));
            await ExecuteAsync("DELETE FROM dbo.Person WHERE Id IN (@a, @b, @c)", ("@a", driver), ("@b", passenger), ("@c", stranger));
        }
    }

    [Fact]
    public async Task The_database_itself_allows_only_one_open_assignment_per_convoy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, _, _) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var first = await AddVolunteerAsync("One", "Open");
        var second = await AddVolunteerAsync("Two", "Open");

        try
        {
            const string insert = "INSERT INTO dbo.ConvoyLeaderAssignment (ConvoyId, PersonId, [From]) VALUES (@id, @person, SYSUTCDATETIME())";
            await ExecuteAsync(insert, ("@id", id), ("@person", first));

            var second_open = () => ExecuteAsync(insert, ("@id", id), ("@person", second));

            await second_open.Should().ThrowAsync<SqlException>();
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.ConvoyLeaderAssignment WHERE ConvoyId = @id", ("@id", id));
            await RemoveConvoyAsync(id);
            await ExecuteAsync("DELETE FROM dbo.Person WHERE Id IN (@a, @b)", ("@a", first), ("@b", second));
        }
    }
}

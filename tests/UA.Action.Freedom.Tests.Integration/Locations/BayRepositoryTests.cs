using AwesomeAssertions;
using UA.Action.Freedom.Application.Locations;
using UA.Action.Freedom.Data.Locations;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Locations;

/// <summary>
/// The Dapper <see cref="BayRepository"/> against a real <c>dbo.Bay</c>, including the
/// per-location unique-code constraint. Skips itself when the local stack is not up.
/// </summary>
[Trait("Category", "Integration")]
public class BayRepositoryTests
{
    private static async Task<BayRepository> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync("SELECT COUNT(1) FROM dbo.Bay", cancellationToken);
        return new BayRepository(ConnectionFactory());
    }

    private static Task<int> AddLocationAsync() => ScalarAsync(
        "INSERT INTO dbo.Location (Name) VALUES ('Integration Depot'); SELECT CAST(SCOPE_IDENTITY() AS int);");

    private static Task RemoveLocationAsync(int id) =>
        ExecuteAsync("DELETE FROM dbo.Location WHERE Id = @id", ("@id", id));

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

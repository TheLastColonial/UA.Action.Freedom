using AwesomeAssertions;
using UA.Action.Freedom.Application.Locations;
using UA.Action.Freedom.Data.Locations;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Locations;

/// <summary>
/// The Dapper <see cref="LocationRepository"/> against a real <c>dbo.Location</c>. Skips itself
/// when the local stack is not up.
/// </summary>
[Trait("Category", "Integration")]
public class LocationRepositoryTests
{
    private static async Task<LocationRepository> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync("SELECT COUNT(1) FROM dbo.Location", cancellationToken);
        return new LocationRepository(ConnectionFactory());
    }

    private static LocationReadModel ANewLocation() => new(
        Id: 0, Name: "Integration Depot", House: "Unit 4", Street: "Cross Road",
        City: "Coventry", Country: "United Kingdom", Postcode: "CV1 2AB");

    private static Task RemoveAsync(int id) =>
        ExecuteAsync("DELETE FROM dbo.Location WHERE Id = @id", ("@id", id));

    [Fact]
    public async Task Round_trips_a_location_and_hands_back_the_identifier_it_assigned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);

        var id = await repository.AddAsync(ANewLocation(), cancellationToken);

        try
        {
            id.Should().BeGreaterThan(0);

            var stored = await repository.GetByIdAsync(id, cancellationToken);

            stored.Should().Be(ANewLocation() with { Id = id });
        }
        finally
        {
            await RemoveAsync(id);
        }
    }

    [Fact]
    public async Task Update_changes_the_row_and_reports_whether_one_matched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(ANewLocation(), cancellationToken);

        try
        {
            var changed = ANewLocation() with { Id = id, Name = "Renamed Depot" };
            var updated = await repository.UpdateAsync(changed, cancellationToken);
            var missing = await repository.UpdateAsync(ANewLocation() with { Id = 999_999 }, cancellationToken);

            updated.Should().BeTrue();
            missing.Should().BeFalse();
            (await repository.GetByIdAsync(id, cancellationToken)).Should().Be(changed);
        }
        finally
        {
            await RemoveAsync(id);
        }
    }

    [Fact]
    public async Task Exists_and_Delete_track_the_row_lifecycle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(ANewLocation(), cancellationToken);

        (await repository.ExistsAsync(id, cancellationToken)).Should().BeTrue();
        (await repository.DeleteAsync(id, cancellationToken)).Should().BeTrue();
        (await repository.ExistsAsync(id, cancellationToken)).Should().BeFalse();
        (await repository.DeleteAsync(id, cancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task List_returns_a_stored_location()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = await repository.AddAsync(ANewLocation(), cancellationToken);

        try
        {
            var page = await repository.ListAsync(1, 200, cancellationToken);

            page.Should().Contain(location => location.Id == id);
        }
        finally
        {
            await RemoveAsync(id);
        }
    }
}

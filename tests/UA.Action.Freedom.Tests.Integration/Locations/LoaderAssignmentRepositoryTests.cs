using AwesomeAssertions;
using UA.Action.Freedom.Application.Locations;
using UA.Action.Freedom.Data.Locations;
using static UA.Action.Freedom.Tests.Integration.Convoys.ConvoyFixtures;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Locations;

/// <summary>
/// <see cref="LoaderAssignmentRepository"/> against a real database: the open-assignment rule is a filtered unique index,
/// and removal closes the row so the history survives (O14, O31, ADR 0010).
/// </summary>
[Trait("Category", "Integration")]
public class LoaderAssignmentRepositoryTests
{
    private static async Task<(LoaderAssignmentRepository Loaders, LocationRepository Locations)> ConnectOrSkipAsync(
        CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync(Probe + "SELECT COUNT(1) FROM dbo.LoaderLocationAssignment;", cancellationToken);
        return (
            new LoaderAssignmentRepository(ConnectionFactory(), Unattributed),
            new LocationRepository(ConnectionFactory(), Unattributed));
    }

    private static LocationReadModel ALocation() => new(0, "Scope test hub", null, null, "Coventry", "United Kingdom", null);

    [Fact]
    public async Task Assigning_twice_is_refused_and_removal_keeps_the_row_closed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (loaders, locations) = await ConnectOrSkipAsync(cancellationToken);
        var locationId = await locations.AddAsync(ALocation(), cancellationToken);
        var person = await AddVolunteerAsync("Scoped", "Loader");
        var first = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);

        try
        {
            (await loaders.AssignAsync(locationId, person, first, cancellationToken)).Should().Be(AssignLoaderResult.Assigned);
            (await loaders.AssignAsync(locationId, person, first, cancellationToken)).Should().Be(AssignLoaderResult.AlreadyAssigned);
            (await loaders.ManagesAsync(person, locationId, cancellationToken)).Should().BeTrue();
            (await loaders.ManagedLocationIdsAsync(person, cancellationToken)).Should().Equal(locationId);

            (await loaders.UnassignAsync(locationId, person, first.AddDays(1), cancellationToken)).Should().BeTrue();
            (await loaders.UnassignAsync(locationId, person, first.AddDays(1), cancellationToken)).Should().BeFalse();
            (await loaders.ManagesAsync(person, locationId, cancellationToken)).Should().BeFalse();
            (await loaders.ManagedLocationIdsAsync(person, cancellationToken)).Should().BeEmpty();

            (await loaders.AssignAsync(locationId, person, first.AddDays(2), cancellationToken)).Should().Be(AssignLoaderResult.Assigned);
            var history = await loaders.HistoryAsync(locationId, cancellationToken);
            history.Select(a => (a.PersonId, a.Until is null)).Should().Equal((person, true), (person, false));
            history[0].PersonName.Should().Be("Scoped Loader");
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.LoaderLocationAssignment WHERE LocationId = @id", ("@id", locationId));
            await locations.DeleteAsync(locationId, cancellationToken);
            await ExecuteAsync("DELETE FROM dbo.Person WHERE Id = @id", ("@id", person));
        }
    }

    [Fact]
    public async Task Deleting_a_location_takes_its_assignments_with_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (loaders, locations) = await ConnectOrSkipAsync(cancellationToken);
        var locationId = await locations.AddAsync(ALocation(), cancellationToken);
        var person = await AddVolunteerAsync("Scoped", "Loader");

        try
        {
            await loaders.AssignAsync(locationId, person, DateTime.UtcNow, cancellationToken);

            await locations.DeleteAsync(locationId, cancellationToken);

            (await ScalarAsync("SELECT COUNT(1) FROM dbo.LoaderLocationAssignment WHERE LocationId = @id", ("@id", locationId)))
                .Should().Be(0);
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.Person WHERE Id = @id", ("@id", person));
        }
    }
}

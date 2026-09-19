using AwesomeAssertions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Data.Boxes;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Boxes;

/// <summary>
/// The Dapper <see cref="BoxRepository"/> bay-assignment methods against real
/// <c>dbo.BoxBayAssignment</c>. Skips itself when the local stack is not up.
/// </summary>
/// <remarks>
/// The one thing that needs a real database: that assigning a new bay vacates whatever bay the
/// box was already in <em>as one transaction</em>, so a box never has two active assignments —
/// the same shape as <see cref="BoxQrCodeRepositoryTests"/>'s re-issue test.
/// </remarks>
[Trait("Category", "Integration")]
public class BoxBayRepositoryTests
{
    private static async Task<BoxRepository> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync("SELECT COUNT(1) FROM dbo.BoxBayAssignment", cancellationToken);
        return new BoxRepository(ConnectionFactory());
    }

    private static BoxReadModel ANewBox() => new(
        Id: 0, WeightKg: 0, WidthCm: null, DepthCm: null, HeightCm: null, ReceiverRef: null,
        LocationId: null, ValidatedByPersonId: null, ValidatedAt: null);

    private static Task<Guid> AddVolunteerAsync() => SqlTestDatabase.AddVolunteerAsync("Integration", "Loader", isDriver: false);

    private static Task<int> AddLocationAsync() => ScalarAsync(
        "INSERT INTO dbo.Location (Name) VALUES ('Integration Depot'); SELECT CAST(SCOPE_IDENTITY() AS int);");

    private static Task<int> AddBayAsync(int locationId, string code) => ScalarAsync(
        "INSERT INTO dbo.Bay (LocationId, Code) VALUES (@locationId, @code); SELECT CAST(SCOPE_IDENTITY() AS int);",
        ("@locationId", locationId),
        ("@code", code));

    private static Task RemoveBoxAsync(int id) =>
        ExecuteAsync("DELETE FROM dbo.Box WHERE Id = @id", ("@id", id));

    private static Task RemoveLocationAsync(int id) =>
        ExecuteAsync("DELETE FROM dbo.Location WHERE Id = @id", ("@id", id));

    private static Task RemoveVolunteerAsync(Guid id) =>
        ExecuteAsync("DELETE FROM dbo.Person WHERE Id = @id", ("@id", id));

    [Fact]
    public async Task Assigning_a_bay_and_reading_it_back_as_the_active_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var loader = await AddVolunteerAsync();
        var location = await AddLocationAsync();
        var bay = await AddBayAsync(location, "A1");
        var boxId = await repository.AddAsync(ANewBox(), cancellationToken);

        try
        {
            var assignedAt = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

            var assigned = await repository.AssignBayAsync(boxId, bay, loader, assignedAt, cancellationToken);
            assigned.BayId.Should().Be(bay);
            assigned.Active.Should().BeTrue();

            var active = await repository.GetActiveBayAssignmentAsync(boxId, cancellationToken);
            active.Should().NotBeNull();
            active!.BayId.Should().Be(bay);
            active.AssignedByPersonId.Should().Be(loader);
        }
        finally
        {
            await RemoveBoxAsync(boxId);
            await RemoveLocationAsync(location);
            await RemoveVolunteerAsync(loader);
        }
    }

    [Fact]
    public async Task Re_assigning_vacates_the_previous_row_in_one_transaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var loader = await AddVolunteerAsync();
        var location = await AddLocationAsync();
        var firstBay = await AddBayAsync(location, "A1");
        var secondBay = await AddBayAsync(location, "A2");
        var boxId = await repository.AddAsync(ANewBox(), cancellationToken);

        try
        {
            await repository.AssignBayAsync(boxId, firstBay, loader, DateTime.UtcNow, cancellationToken);
            await repository.AssignBayAsync(boxId, secondBay, loader, DateTime.UtcNow, cancellationToken);

            var active = await repository.GetActiveBayAssignmentAsync(boxId, cancellationToken);
            active!.BayId.Should().Be(secondBay);

            (await ScalarAsync("SELECT COUNT(1) FROM dbo.BoxBayAssignment WHERE BoxId = @id", ("@id", boxId)))
                .Should().Be(2);
            (await ScalarAsync(
                "SELECT COUNT(1) FROM dbo.BoxBayAssignment WHERE BoxId = @id AND VacatedAt IS NULL", ("@id", boxId)))
                .Should().Be(1);
        }
        finally
        {
            await RemoveBoxAsync(boxId);
            await RemoveLocationAsync(location);
            await RemoveVolunteerAsync(loader);
        }
    }

    [Fact]
    public async Task Vacating_leaves_the_history_but_no_active_assignment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var loader = await AddVolunteerAsync();
        var location = await AddLocationAsync();
        var bay = await AddBayAsync(location, "A1");
        var boxId = await repository.AddAsync(ANewBox(), cancellationToken);

        try
        {
            await repository.AssignBayAsync(boxId, bay, loader, DateTime.UtcNow, cancellationToken);

            (await repository.VacateActiveBayAssignmentAsync(boxId, cancellationToken)).Should().BeTrue();

            (await repository.GetActiveBayAssignmentAsync(boxId, cancellationToken)).Should().BeNull();
            var history = await repository.ListBayAssignmentHistoryAsync(boxId, cancellationToken);
            history.Should().ContainSingle(entry => entry.BayId == bay && !entry.Active);
        }
        finally
        {
            await RemoveBoxAsync(boxId);
            await RemoveLocationAsync(location);
            await RemoveVolunteerAsync(loader);
        }
    }

    [Fact]
    public async Task Vacating_when_nothing_is_active_returns_false()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var boxId = await repository.AddAsync(ANewBox(), cancellationToken);

        try
        {
            (await repository.VacateActiveBayAssignmentAsync(boxId, cancellationToken)).Should().BeFalse();
        }
        finally
        {
            await RemoveBoxAsync(boxId);
        }
    }

    [Fact]
    public async Task Deleting_the_box_takes_its_bay_history_with_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var loader = await AddVolunteerAsync();
        var location = await AddLocationAsync();
        var bay = await AddBayAsync(location, "A1");
        var boxId = await repository.AddAsync(ANewBox(), cancellationToken);

        try
        {
            await repository.AssignBayAsync(boxId, bay, loader, DateTime.UtcNow, cancellationToken);

            (await repository.DeleteAsync(boxId, cancellationToken)).Should().BeTrue();

            (await ScalarAsync("SELECT COUNT(1) FROM dbo.BoxBayAssignment WHERE BoxId = @id", ("@id", boxId)))
                .Should().Be(0);
        }
        finally
        {
            await RemoveLocationAsync(location);
            await RemoveVolunteerAsync(loader);
        }
    }
}

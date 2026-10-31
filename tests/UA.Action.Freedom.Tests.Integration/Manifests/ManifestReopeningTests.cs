using AwesomeAssertions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Data.Boxes;
using UA.Action.Freedom.Data.Convoys;
using UA.Action.Freedom.Data.Manifests;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.Convoys.ConvoyFixtures;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Manifests;

/// <summary>
/// Any change to a vehicle's load sends its approved manifest back to Proposed, in the same transaction as the change
/// (ADR 0004, X3), against the real database. Skips itself when the local stack is not up.
/// </summary>
[Trait("Category", "Integration")]
public class ManifestReopeningTests
{
    private sealed record World(
        ManifestRepository Manifests,
        ConvoyVehicleRepository TruckList,
        BoxRepository Boxes,
        BoxOutcomeRepository Outcomes,
        int ConvoyId,
        string VinA,
        string VinB,
        string ManifestA,
        string ManifestB,
        int BoxId,
        Guid Volunteer);

    private static async Task<World> ArrangeAsync(
        CancellationToken cancellationToken, ManifestStatus status = ManifestStatus.Approved)
    {
        await SkipUnlessReachableAsync(
            "SELECT COUNT(1) FROM dbo.Manifest; SELECT COUNT(1) FROM dbo.BoxOutcome; SELECT COUNT(1) FROM dbo.ConvoyVehicleBoxAllocation;",
            cancellationToken);
        await EnsureRecorderAsync();

        var convoys = new ConvoyRepository(ConnectionFactory(), Unattributed);
        var truckList = new ConvoyVehicleRepository(ConnectionFactory(), Unattributed);
        var boxes = new BoxRepository(ConnectionFactory(), Unattributed);
        var volunteer = await AddDriverAsync("Reopen", "Tester");

        var convoyId = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vinA = NewVin();
        var vinB = NewVin();
        foreach (var vin in new[] { vinA, vinB })
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(convoyId, vin, cancellationToken);
        }

        var manifestA = await AddManifestAsync(convoyId, vinA, status);
        var manifestB = await AddManifestAsync(convoyId, vinB, status);
        var boxId = await boxes.AddAsync(new BoxReadModel(0, 0, null, null, null, null, null, null, null), cancellationToken);

        return new World(
            new ManifestRepository(ConnectionFactory(), Unattributed),
            truckList,
            boxes,
            new BoxOutcomeRepository(ConnectionFactory(), AttributedTo(RecorderId)),
            convoyId, vinA, vinB, manifestA, manifestB, boxId, volunteer);
    }

    private static async Task CleanUpAsync(World world, params int[] extraBoxes)
    {
        await RemoveManifestsAsync(world.ConvoyId);
        await ExecuteAsync("DELETE FROM dbo.BoxOutcome WHERE ConvoyId = @c", ("@c", world.ConvoyId));
        await ExecuteAsync("DELETE FROM dbo.ConvoyVehicleBoxAllocation WHERE ConvoyId = @c", ("@c", world.ConvoyId));

        foreach (var boxId in extraBoxes.Append(world.BoxId).OrderByDescending(id => id))
        {
            await ExecuteAsync("DELETE FROM dbo.Box WHERE Id = @id", ("@id", boxId));
        }

        await RemoveVehicleAsync(world.VinA);
        await RemoveVehicleAsync(world.VinB);
        await RemoveConvoyAsync(world.ConvoyId);
        await RemovePeopleAsync(world.Volunteer);
    }

    private static async Task<ManifestReadModel> ReadAsync(World world, string id, CancellationToken cancellationToken) =>
        (await world.Manifests.GetByIdAsync(id, cancellationToken))!;

    [Fact]
    public async Task Allocating_a_box_reopens_the_approved_manifest_of_the_vehicle_it_joins()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            await world.TruckList.AllocateBoxAsync(world.ConvoyId, world.VinA, world.BoxId, cancellationToken);

            var reopened = await ReadAsync(world, world.ManifestA, cancellationToken);
            reopened.Status.Should().Be(ManifestStatus.Proposed);
            reopened.ReopenedAt.Should().NotBeNull();
            (await ReadAsync(world, world.ManifestB, cancellationToken)).Status.Should().Be(ManifestStatus.Approved);
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }

    [Fact]
    public async Task A_manifest_nobody_has_approved_is_not_touched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken, ManifestStatus.Rejected);

        try
        {
            await world.TruckList.AllocateBoxAsync(world.ConvoyId, world.VinA, world.BoxId, cancellationToken);

            var manifest = await ReadAsync(world, world.ManifestA, cancellationToken);
            manifest.Status.Should().Be(ManifestStatus.Rejected);
            manifest.ReopenedAt.Should().BeNull();
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }

    [Fact]
    public async Task Moving_a_box_reopens_the_manifests_of_both_vehicles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            await world.TruckList.AllocateBoxAsync(world.ConvoyId, world.VinA, world.BoxId, cancellationToken);
            await world.Manifests.ApproveAsync(world.ManifestA, cancellationToken);

            await world.TruckList.AllocateBoxAsync(world.ConvoyId, world.VinB, world.BoxId, cancellationToken);

            (await ReadAsync(world, world.ManifestA, cancellationToken)).Status.Should().Be(ManifestStatus.Proposed);
            (await ReadAsync(world, world.ManifestB, cancellationToken)).Status.Should().Be(ManifestStatus.Proposed);
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }

    [Fact]
    public async Task Removing_a_box_reopens_the_manifest_and_a_second_approval_clears_the_note()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            await world.TruckList.AllocateBoxAsync(world.ConvoyId, world.VinA, world.BoxId, cancellationToken);
            await world.Manifests.ApproveAsync(world.ManifestA, cancellationToken);

            (await world.TruckList.RemoveBoxAsync(world.ConvoyId, world.VinA, world.BoxId, cancellationToken)).Should().BeTrue();

            var reopened = await ReadAsync(world, world.ManifestA, cancellationToken);
            reopened.Status.Should().Be(ManifestStatus.Proposed);
            reopened.ReopenedAt.Should().NotBeNull();

            (await world.Manifests.ApproveAsync(world.ManifestA, cancellationToken)).Should().BeTrue();
            (await ReadAsync(world, world.ManifestA, cancellationToken)).ReopenedAt.Should().BeNull();
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }

    [Fact]
    public async Task Removing_a_box_that_was_not_on_the_vehicle_reopens_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            (await world.TruckList.RemoveBoxAsync(world.ConvoyId, world.VinA, world.BoxId, cancellationToken)).Should().BeFalse();

            (await ReadAsync(world, world.ManifestA, cancellationToken)).Status.Should().Be(ManifestStatus.Approved);
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }

    [Fact]
    public async Task Replacing_an_attested_box_reopens_the_manifest_of_the_vehicle_carrying_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);
        int? replacementId = null;

        try
        {
            await world.TruckList.AllocateBoxAsync(world.ConvoyId, world.VinA, world.BoxId, cancellationToken);
            await world.Boxes.ValidateAsync(world.BoxId, world.Volunteer, 24, null, null, null, DateTime.UtcNow, cancellationToken);
            await world.Manifests.ApproveAsync(world.ManifestA, cancellationToken);

            replacementId = await world.Boxes.ReplaceAsync(world.BoxId, cancellationToken);

            replacementId.Should().NotBeNull();
            (await ReadAsync(world, world.ManifestA, cancellationToken)).Status.Should().Be(ManifestStatus.Proposed);
        }
        finally
        {
            await CleanUpAsync(world, replacementId is { } id ? [id] : []);
        }
    }

    [Theory]
    [InlineData(BoxOutcome.Seized, true)]
    [InlineData(BoxOutcome.Undeliverable, true)]
    [InlineData(BoxOutcome.Delivered, false)]
    public async Task A_box_that_leaves_the_load_reopens_the_manifest_and_a_delivery_does_not(
        BoxOutcome outcome, bool reopens)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            await world.TruckList.AllocateBoxAsync(world.ConvoyId, world.VinA, world.BoxId, cancellationToken);
            await world.Manifests.ApproveAsync(world.ManifestA, cancellationToken);

            (await world.Outcomes.RecordAsync(
                    new RecordBoxOutcome(
                        world.ConvoyId, world.VinA, world.BoxId, null, outcome,
                        outcome == BoxOutcome.Undeliverable ? "stolen" : null, null),
                    cancellationToken))
                .Should().Be(RecordBoxOutcomeResult.Recorded);

            (await ReadAsync(world, world.ManifestA, cancellationToken)).Status
                .Should().Be(reopens ? ManifestStatus.Proposed : ManifestStatus.Approved);
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }
}

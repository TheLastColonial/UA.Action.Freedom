using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Declarations;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Declarations;

/// <summary>
/// The re-declare task (D13, O21): one for every stale declaration on the convoy, with the resolution
/// that fits the instrument; and withdrawing the stale declaration, which clears it.
/// </summary>
public class RedeclareTaskTests
{
    private const string VinA = "WVWZZZ1JZXW000001";

    private const string VinB = "WVWZZZ1JZXW000002";

    private static readonly Guid Receiver = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static LoadSnapshot ALoad(string vin, params int[] boxIds) => new(
        LoadSnapshot.CurrentVersion, vin, false,
        [.. boxIds.Select(id => new LoadBox(id, 10, null, false, []))]);

    private static DeclarationReadModel ADeclaration(
        int id, string vin, DeclarationKind kind, DeclarationStatus status, LoadSnapshot writtenFrom,
        Guid? receiverRef = null) => new(
        id, 42, vin, kind, status, receiverRef, "REF", null, null, null, null, null,
        LoadSnapshotJson.Write(writtenFrom), writtenFrom.Version);

    private sealed record Fixture(
        IConvoyRepository Convoys,
        IConvoyVehicleRepository TruckList,
        IDeclarationRepository Declarations,
        IVehicleLoadReader Loads);

    private static Fixture AConvoyWithTwoVehicles(
        IReadOnlyList<DeclarationReadModel> onA, IReadOnlyList<DeclarationReadModel> onB)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(42, Arg.Any<CancellationToken>()).Returns(
            new ConvoyReadModel(42, DateTime.UtcNow, DateTime.UtcNow.AddDays(4), null));
        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.ListAsync(42, Arg.Any<CancellationToken>()).Returns(
        [
            new ConvoyVehicleReadModel(VinA, "AB12 CDE", 3000, 2, 0),
            new ConvoyVehicleReadModel(VinB, "AB12 CDF", 3000, 2, 0),
        ]);
        truckList.GetAsync(42, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            call => new ConvoyVehicleReadModel(call.ArgAt<string>(1), "AB12 CDE", 3000, 2, 0));
        var declarations = Substitute.For<IDeclarationRepository>();
        declarations.ListAsync(42, VinA, Arg.Any<CancellationToken>()).Returns(onA);
        declarations.ListAsync(42, VinB, Arg.Any<CancellationToken>()).Returns(onB);
        var loads = Substitute.For<IVehicleLoadReader>();
        loads.ReadAsync(42, VinA, Arg.Any<CancellationToken>()).Returns(ALoad(VinA, 1, 2));
        loads.ReadAsync(42, VinB, Arg.Any<CancellationToken>()).Returns(ALoad(VinB, 3));
        return new Fixture(convoys, truckList, declarations, loads);
    }

    private static Task<IReadOnlyList<RedeclareTaskReadModel>?> TasksAsync(Fixture fixture) =>
        new ListRedeclareTasksHandler(fixture.Convoys, fixture.TruckList, fixture.Declarations, fixture.Loads)
            .HandleAsync(new ListRedeclareTasksQuery(42), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Every_stale_declaration_on_the_convoy_is_a_task()
    {
        var fixture = AConvoyWithTwoVehicles(
            [ADeclaration(1, VinA, DeclarationKind.Gmr, DeclarationStatus.Filed, ALoad(VinA, 1))],
            [ADeclaration(2, VinB, DeclarationKind.Gmr, DeclarationStatus.Accepted, ALoad(VinB, 3, 4))]);

        var tasks = await TasksAsync(fixture);

        tasks!.Select(task => (task.Vin, task.DeclarationId)).Should().Equal((VinA, 1), (VinB, 2));
    }

    [Fact]
    public async Task A_declaration_that_is_current_is_not_a_task()
    {
        var fixture = AConvoyWithTwoVehicles(
            [ADeclaration(1, VinA, DeclarationKind.Gmr, DeclarationStatus.Filed, ALoad(VinA, 1, 2))], []);

        (await TasksAsync(fixture))!.Should().BeEmpty();
    }

    [Theory]
    [InlineData(DeclarationKind.Gmr, RedeclareResolution.UpdateOrRecreate)]
    [InlineData(DeclarationKind.Ens, RedeclareResolution.InvalidateAndRefile)]
    [InlineData(DeclarationKind.Elo, RedeclareResolution.NewEnvelopeAgainstNewMrn)]
    [InlineData(DeclarationKind.GoodsList, RedeclareResolution.PrepareNewListAndHoldAtHub)]
    public async Task The_task_offers_the_resolution_that_fits_the_instrument(
        DeclarationKind kind, RedeclareResolution resolution)
    {
        var fixture = AConvoyWithTwoVehicles(
            [ADeclaration(1, VinA, kind, DeclarationStatus.Accepted, ALoad(VinA, 1), kind == DeclarationKind.GoodsList ? Receiver : null)],
            []);

        var task = (await TasksAsync(fixture))!.Single();

        task.Resolution.Should().Be(resolution);
        task.Kind.Should().Be(kind);
    }

    [Fact]
    public async Task A_convoy_that_does_not_exist_has_no_tasks()
    {
        var fixture = AConvoyWithTwoVehicles([], []);
        fixture.Convoys.GetByIdAsync(42, Arg.Any<CancellationToken>()).Returns((ConvoyReadModel?)null);

        (await TasksAsync(fixture)).Should().BeNull();
    }

    // --- withdrawing ------------------------------------------------------------------------------

    private static Task<WithdrawDeclarationOutcome> WithdrawAsync(Fixture fixture, int declarationId) =>
        new WithdrawDeclarationHandler(fixture.TruckList, fixture.Declarations, fixture.Loads)
            .HandleAsync(new WithdrawDeclarationCommand(42, VinA, declarationId), TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_stale_declaration_is_withdrawn_and_a_new_draft_started_for_its_scope()
    {
        var fixture = AConvoyWithTwoVehicles(
            [ADeclaration(1, VinA, DeclarationKind.GoodsList, DeclarationStatus.Filed, ALoad(VinA, 1), Receiver)], []);
        fixture.Declarations.WithdrawAndRedraftAsync(
                42, VinA, DeclarationKind.GoodsList, Receiver, Arg.Any<CancellationToken>())
            .Returns(true);

        var outcome = await WithdrawAsync(fixture, 1);

        outcome.Should().Be(WithdrawDeclarationOutcome.Withdrawn);
    }

    [Fact]
    public async Task A_declaration_that_is_not_stale_is_not_withdrawn()
    {
        var fixture = AConvoyWithTwoVehicles(
            [ADeclaration(1, VinA, DeclarationKind.Gmr, DeclarationStatus.Filed, ALoad(VinA, 1, 2))], []);

        var outcome = await WithdrawAsync(fixture, 1);

        outcome.Should().Be(WithdrawDeclarationOutcome.NotStale);
        await fixture.Declarations.DidNotReceive().WithdrawAndRedraftAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<DeclarationKind>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_declaration_that_is_not_on_this_vehicle_is_not_found()
    {
        var fixture = AConvoyWithTwoVehicles([], []);

        (await WithdrawAsync(fixture, 99)).Should().Be(WithdrawDeclarationOutcome.NotFound);
    }

    [Fact]
    public async Task A_vehicle_that_is_not_on_the_convoy_is_not_found()
    {
        var fixture = AConvoyWithTwoVehicles([], []);
        fixture.TruckList.GetAsync(42, VinA, Arg.Any<CancellationToken>()).Returns((ConvoyVehicleReadModel?)null);

        (await WithdrawAsync(fixture, 1)).Should().Be(WithdrawDeclarationOutcome.NotFound);
    }
}

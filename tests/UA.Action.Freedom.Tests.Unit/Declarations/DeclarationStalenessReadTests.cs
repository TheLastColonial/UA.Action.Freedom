using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Declarations;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Declarations;

/// <summary>
/// Staleness is derived when a declaration is read (ADR 0005): a filed or accepted declaration whose
/// snapshot differs from the load now reads Stale, and nothing is written to say so.
/// </summary>
public class DeclarationStalenessReadTests
{
    private const string Vin = "WVWZZZ1JZXW000001";

    private static LoadSnapshot ALoad(params int[] boxIds) => new(
        LoadSnapshot.CurrentVersion, Vin, false,
        [.. boxIds.Select(id => new LoadBox(id, 10, null, false, []))]);

    private static DeclarationReadModel ADeclaration(
        DeclarationStatus status, LoadSnapshot? writtenFrom, DeclarationKind kind = DeclarationKind.Gmr) => new(
        9, 42, Vin, kind, status, null, "REF-1", null, null, null, null, null,
        writtenFrom is null ? null : LoadSnapshotJson.Write(writtenFrom),
        writtenFrom?.Version);

    private static async Task<IReadOnlyList<DeclarationReadModel>?> ReadAsync(
        LoadSnapshot? now, params DeclarationReadModel[] stored)
    {
        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.GetAsync(42, Vin, Arg.Any<CancellationToken>())
            .Returns(new ConvoyVehicleReadModel(Vin, "AB12 CDE", 3000, 2, 0));
        var declarations = Substitute.For<IDeclarationRepository>();
        declarations.ListAsync(42, Vin, Arg.Any<CancellationToken>()).Returns(stored);
        var loads = Substitute.For<IVehicleLoadReader>();
        loads.ReadAsync(42, Vin, Arg.Any<CancellationToken>()).Returns(now);

        return await new ListDeclarationsHandler(truckList, declarations, loads)
            .HandleAsync(new ListDeclarationsQuery(42, Vin), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(DeclarationStatus.Filed)]
    [InlineData(DeclarationStatus.Accepted)]
    public async Task A_filed_or_accepted_declaration_reads_stale_once_the_load_has_changed(DeclarationStatus status)
    {
        var read = await ReadAsync(ALoad(1, 2), ADeclaration(status, ALoad(1)));

        read!.Single().Status.Should().Be(DeclarationStatus.Stale);
    }

    [Fact]
    public async Task A_declaration_whose_load_is_unchanged_keeps_its_status()
    {
        var read = await ReadAsync(ALoad(1), ADeclaration(DeclarationStatus.Accepted, ALoad(1)));

        read!.Single().Status.Should().Be(DeclarationStatus.Accepted);
    }

    [Theory]
    [InlineData(DeclarationStatus.Draft)]
    [InlineData(DeclarationStatus.ReadyToFile)]
    [InlineData(DeclarationStatus.Refused)]
    [InlineData(DeclarationStatus.Withdrawn)]
    [InlineData(DeclarationStatus.Closed)]
    public async Task Only_a_filed_or_accepted_declaration_can_read_stale(DeclarationStatus status)
    {
        var read = await ReadAsync(ALoad(1, 2), ADeclaration(status, ALoad(1)));

        read!.Single().Status.Should().Be(status);
    }

    [Fact]
    public async Task A_declaration_with_no_snapshot_has_nothing_to_differ_from()
    {
        var read = await ReadAsync(ALoad(1, 2), ADeclaration(DeclarationStatus.Filed, writtenFrom: null));

        read!.Single().Status.Should().Be(DeclarationStatus.Filed);
    }

    [Fact]
    public async Task Each_declaration_is_judged_against_its_own_snapshot()
    {
        var read = await ReadAsync(
            ALoad(1, 2),
            ADeclaration(DeclarationStatus.Accepted, ALoad(1, 2), DeclarationKind.Ens),
            ADeclaration(DeclarationStatus.Filed, ALoad(1)));

        read!.Select(d => d.Status).Should().Equal(DeclarationStatus.Accepted, DeclarationStatus.Stale);
    }

    [Fact]
    public async Task Reading_never_writes_stale_to_the_database()
    {
        var declarations = Substitute.For<IDeclarationRepository>();
        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.GetAsync(42, Vin, Arg.Any<CancellationToken>())
            .Returns(new ConvoyVehicleReadModel(Vin, "AB12 CDE", 3000, 2, 0));
        declarations.ListAsync(42, Vin, Arg.Any<CancellationToken>())
            .Returns([ADeclaration(DeclarationStatus.Filed, ALoad(1))]);
        var loads = Substitute.For<IVehicleLoadReader>();
        loads.ReadAsync(42, Vin, Arg.Any<CancellationToken>()).Returns(ALoad(1, 2));

        await new ListDeclarationsHandler(truckList, declarations, loads)
            .HandleAsync(new ListDeclarationsQuery(42, Vin), TestContext.Current.CancellationToken);

        declarations.ReceivedCalls().Select(call => call.GetMethodInfo().Name).Should().OnlyContain(name => name == "ListAsync");
    }
}

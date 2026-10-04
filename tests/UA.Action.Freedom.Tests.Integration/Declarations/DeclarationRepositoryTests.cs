using AwesomeAssertions;
using UA.Action.Freedom.Application.Declarations;
using UA.Action.Freedom.Data.Convoys;
using UA.Action.Freedom.Data.Declarations;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.Convoys.ConvoyFixtures;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Declarations;

/// <summary>
/// <c>dbo.Declaration</c> against a real database: conditional transitions, the write-once reference
/// and the one-current-per-scope rule (ADR 0005).
/// </summary>
[Trait("Category", "Integration")]
public class DeclarationRepositoryTests
{
    private static async Task<(ConvoyRepository Convoys, ConvoyVehicleRepository TruckList, DeclarationRepository Declarations)>
        ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync(Probe + "SELECT COUNT(1) FROM dbo.Declaration;", cancellationToken);
        await EnsureRecorderAsync();

        return (
            new ConvoyRepository(ConnectionFactory(), Unattributed),
            new ConvoyVehicleRepository(ConnectionFactory(), Unattributed),
            new DeclarationRepository(ConnectionFactory(), Unattributed));
    }

    private static async Task<(int ConvoyId, string Vin)> AVehicleOnAConvoyAsync(
        ConvoyRepository convoys, ConvoyVehicleRepository truckList, CancellationToken cancellationToken)
    {
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        await AddVehicleAsync(vin);
        await truckList.AddAsync(id, vin, cancellationToken);
        return (id, vin);
    }

    [Fact]
    public async Task Records_a_reference_files_the_declaration_and_will_not_overwrite_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, declarations) = await ConnectOrSkipAsync(cancellationToken);
        var (convoyId, vin) = await AVehicleOnAConvoyAsync(convoys, truckList, cancellationToken);

        try
        {
            (await declarations.RecordReferenceAsync(convoyId, vin, DeclarationKind.Gmr, null, "GMR-1", cancellationToken))
                .Should().Be(RecordReferenceResult.Recorded);
            (await declarations.RecordReferenceAsync(convoyId, vin, DeclarationKind.Gmr, null, "GMR-2", cancellationToken))
                .Should().Be(RecordReferenceResult.AlreadyRecorded);

            var current = await declarations.GetCurrentAsync(convoyId, vin, DeclarationKind.Gmr, null, cancellationToken);
            current!.Reference.Should().Be("GMR-1");
            current.Status.Should().Be(DeclarationStatus.Filed);
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }

    [Fact]
    public async Task An_ens_goes_straight_to_accepted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, declarations) = await ConnectOrSkipAsync(cancellationToken);
        var (convoyId, vin) = await AVehicleOnAConvoyAsync(convoys, truckList, cancellationToken);

        try
        {
            await declarations.RecordReferenceAsync(
                convoyId, vin, DeclarationKind.Ens, null, "25FR17551780961AT5", cancellationToken);

            (await declarations.GetCurrentAsync(convoyId, vin, DeclarationKind.Ens, null, cancellationToken))!
                .Status.Should().Be(DeclarationStatus.Accepted);
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }

    [Fact]
    public async Task Refuses_a_vehicle_that_is_not_on_the_convoy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, declarations) = await ConnectOrSkipAsync(cancellationToken);
        var (convoyId, vin) = await AVehicleOnAConvoyAsync(convoys, truckList, cancellationToken);

        try
        {
            (await declarations.RecordReferenceAsync(
                    convoyId, "NOSUCHVIN000000", DeclarationKind.Gmr, null, "GMR-1", cancellationToken))
                .Should().Be(RecordReferenceResult.VehicleNotOnConvoy);
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }

    [Fact]
    public async Task Only_a_filed_declaration_can_be_refused_and_a_refused_one_can_be_recorded_afresh()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, declarations) = await ConnectOrSkipAsync(cancellationToken);
        var (convoyId, vin) = await AVehicleOnAConvoyAsync(convoys, truckList, cancellationToken);

        try
        {
            await declarations.RecordReferenceAsync(convoyId, vin, DeclarationKind.Gmr, null, "GMR-1", cancellationToken);
            var filed = await declarations.GetCurrentAsync(convoyId, vin, DeclarationKind.Gmr, null, cancellationToken);

            (await declarations.RefuseAsync(filed!.Id, "data-error", cancellationToken)).Should().BeTrue();
            (await declarations.RefuseAsync(filed.Id, "data-error", cancellationToken)).Should().BeFalse();

            (await declarations.RecordReferenceAsync(convoyId, vin, DeclarationKind.Gmr, null, "GMR-2", cancellationToken))
                .Should().Be(RecordReferenceResult.Recorded);
            var corrected = await declarations.GetCurrentAsync(convoyId, vin, DeclarationKind.Gmr, null, cancellationToken);
            corrected!.Reference.Should().Be("GMR-2");
            corrected.ReasonCode.Should().BeNull();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }

    [Fact]
    public async Task Withdrawing_keeps_the_row_and_its_reference_and_lets_a_replacement_in()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, declarations) = await ConnectOrSkipAsync(cancellationToken);
        var (convoyId, vin) = await AVehicleOnAConvoyAsync(convoys, truckList, cancellationToken);

        try
        {
            await declarations.RecordReferenceAsync(
                convoyId, vin, DeclarationKind.Ens, null, "25FR17551780961AT5", cancellationToken);

            (await declarations.WithdrawAsync(convoyId, vin, DeclarationKind.Ens, null, cancellationToken)).Should().BeTrue();
            (await declarations.WithdrawAsync(convoyId, vin, DeclarationKind.Ens, null, cancellationToken)).Should().BeFalse();
            (await declarations.RecordReferenceAsync(
                    convoyId, vin, DeclarationKind.Ens, null, "26GB99999999999ZZ9", cancellationToken))
                .Should().Be(RecordReferenceResult.Recorded);

            var all = await declarations.ListAsync(convoyId, vin, cancellationToken);
            all.Should().HaveCount(2);
            all.Should().ContainSingle(row => row.Status == DeclarationStatus.Withdrawn)
                .Which.Reference.Should().Be("25FR17551780961AT5");
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }

    [Fact]
    public async Task The_database_allows_one_current_declaration_per_scope_and_kind()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, declarations) = await ConnectOrSkipAsync(cancellationToken);
        var (convoyId, vin) = await AVehicleOnAConvoyAsync(convoys, truckList, cancellationToken);

        try
        {
            await declarations.RecordReferenceAsync(convoyId, vin, DeclarationKind.Gmr, null, "GMR-1", cancellationToken);

            var act = () => ExecuteAsync(
                "INSERT INTO dbo.Declaration (ConvoyId, Vin, Kind, Status) VALUES (@c, @v, @k, 0)",
                ("@c", convoyId), ("@v", vin), ("@k", (int)DeclarationKind.Gmr));

            await act.Should().ThrowAsync<Exception>();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }
}

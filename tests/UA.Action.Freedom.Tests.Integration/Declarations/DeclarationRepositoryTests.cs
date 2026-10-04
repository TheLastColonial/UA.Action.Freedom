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

    // ---- the snapshot (plan 09) -------------------------------------------------------------------

    [Fact]
    public async Task Marking_ready_stores_the_snapshot_and_reads_it_back()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, declarations) = await ConnectOrSkipAsync(cancellationToken);
        var (convoyId, vin) = await AVehicleOnAConvoyAsync(convoys, truckList, cancellationToken);

        try
        {
            (await declarations.MarkReadyAsync(convoyId, vin, DeclarationKind.Gmr, null, "{\"vin\":\"x\"}", 1, cancellationToken))
                .Should().Be(MarkReadyResult.Ready);

            var current = await declarations.GetCurrentAsync(convoyId, vin, DeclarationKind.Gmr, null, cancellationToken);
            current!.Status.Should().Be(DeclarationStatus.ReadyToFile);
            current.SnapshotJson.Should().Be("{\"vin\":\"x\"}");
            current.SnapshotVersion.Should().Be(1);
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }

    [Fact]
    public async Task Marking_ready_again_refreshes_the_snapshot_but_not_once_filed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, declarations) = await ConnectOrSkipAsync(cancellationToken);
        var (convoyId, vin) = await AVehicleOnAConvoyAsync(convoys, truckList, cancellationToken);

        try
        {
            await declarations.MarkReadyAsync(convoyId, vin, DeclarationKind.Gmr, null, "{\"n\":1}", 1, cancellationToken);
            await declarations.MarkReadyAsync(convoyId, vin, DeclarationKind.Gmr, null, "{\"n\":2}", 1, cancellationToken);
            (await declarations.GetCurrentAsync(convoyId, vin, DeclarationKind.Gmr, null, cancellationToken))!
                .SnapshotJson.Should().Be("{\"n\":2}");

            await declarations.RecordReferenceAsync(convoyId, vin, DeclarationKind.Gmr, null, "GMR-1", cancellationToken);

            (await declarations.MarkReadyAsync(convoyId, vin, DeclarationKind.Gmr, null, "{\"n\":3}", 1, cancellationToken))
                .Should().Be(MarkReadyResult.NotPreparable);
            (await declarations.GetCurrentAsync(convoyId, vin, DeclarationKind.Gmr, null, cancellationToken))!
                .SnapshotJson.Should().Be("{\"n\":2}");
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }

    [Fact]
    public async Task A_snapshot_is_stored_once_against_a_declaration_recorded_without_being_marked_ready()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, declarations) = await ConnectOrSkipAsync(cancellationToken);
        var (convoyId, vin) = await AVehicleOnAConvoyAsync(convoys, truckList, cancellationToken);

        try
        {
            await declarations.RecordReferenceAsync(convoyId, vin, DeclarationKind.Gmr, null, "GMR-1", cancellationToken);

            (await declarations.StoreSnapshotAsync(convoyId, vin, DeclarationKind.Gmr, null, "{\"n\":1}", 1, cancellationToken))
                .Should().BeTrue();
            (await declarations.StoreSnapshotAsync(convoyId, vin, DeclarationKind.Gmr, null, "{\"n\":2}", 1, cancellationToken))
                .Should().BeFalse();
            (await declarations.GetCurrentAsync(convoyId, vin, DeclarationKind.Gmr, null, cancellationToken))!
                .SnapshotJson.Should().Be("{\"n\":1}");
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }

    [Fact]
    public async Task A_refused_declaration_recorded_afresh_is_snapshotted_afresh()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, declarations) = await ConnectOrSkipAsync(cancellationToken);
        var (convoyId, vin) = await AVehicleOnAConvoyAsync(convoys, truckList, cancellationToken);

        try
        {
            await declarations.RecordReferenceAsync(convoyId, vin, DeclarationKind.Gmr, null, "GMR-1", cancellationToken);
            await declarations.StoreSnapshotAsync(convoyId, vin, DeclarationKind.Gmr, null, "{\"n\":1}", 1, cancellationToken);
            var id = (await declarations.GetCurrentAsync(convoyId, vin, DeclarationKind.Gmr, null, cancellationToken))!.Id;
            await declarations.RefuseAsync(id, "data-error", cancellationToken);

            await declarations.RecordReferenceAsync(convoyId, vin, DeclarationKind.Gmr, null, "GMR-2", cancellationToken);

            (await declarations.GetCurrentAsync(convoyId, vin, DeclarationKind.Gmr, null, cancellationToken))!
                .SnapshotJson.Should().BeNull();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }

    [Fact]
    public async Task Withdrawing_and_redrafting_keeps_one_current_declaration_and_the_old_reference_as_history()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, declarations) = await ConnectOrSkipAsync(cancellationToken);
        var (convoyId, vin) = await AVehicleOnAConvoyAsync(convoys, truckList, cancellationToken);

        try
        {
            await declarations.RecordReferenceAsync(convoyId, vin, DeclarationKind.Gmr, null, "GMR-1", cancellationToken);

            (await declarations.WithdrawAndRedraftAsync(convoyId, vin, DeclarationKind.Gmr, null, cancellationToken))
                .Should().BeTrue();

            var all = await declarations.ListAsync(convoyId, vin, cancellationToken);
            all.Select(row => row.Status).Should().Equal(DeclarationStatus.Withdrawn, DeclarationStatus.Draft);
            all[0].Reference.Should().Be("GMR-1");
            (await declarations.GetCurrentAsync(convoyId, vin, DeclarationKind.Gmr, null, cancellationToken))!
                .Status.Should().Be(DeclarationStatus.Draft);

            (await declarations.RecordReferenceAsync(convoyId, vin, DeclarationKind.Gmr, null, "GMR-2", cancellationToken))
                .Should().Be(RecordReferenceResult.Recorded);
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }

    [Fact]
    public async Task Nothing_is_withdrawn_or_redrafted_when_the_declaration_was_never_filed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, declarations) = await ConnectOrSkipAsync(cancellationToken);
        var (convoyId, vin) = await AVehicleOnAConvoyAsync(convoys, truckList, cancellationToken);

        try
        {
            await declarations.MarkReadyAsync(convoyId, vin, DeclarationKind.Gmr, null, "{}", 1, cancellationToken);

            (await declarations.WithdrawAndRedraftAsync(convoyId, vin, DeclarationKind.Gmr, null, cancellationToken))
                .Should().BeFalse();
            (await declarations.ListAsync(convoyId, vin, cancellationToken)).Should().ContainSingle();
        }
        finally
        {
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }
}

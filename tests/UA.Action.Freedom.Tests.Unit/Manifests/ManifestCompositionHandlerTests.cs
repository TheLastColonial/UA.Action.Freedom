using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Manifests;

/// <summary>
/// Composing a manifest — its cargo, the crew it reports, and the weight a border check is given.
/// </summary>
public class ManifestCompositionHandlerTests
{
    private const string Id = "MAN-0001";
    private const string Vin = "WVWZZZ1JZXW000001";
    private const int ConvoyId = 42;

    private static readonly Guid Driver = new("2b9c1e40-7d8a-4c31-9f52-6a0b8d3e5c11");

    private static ManifestReadModel AManifest(bool frozen = false) => new(
        Id, ConvoyId, Vin, ManifestStatus.Preparing, null, false,
        frozen ? new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc) : null);

    private static readonly VehicleCargoCapacityReadModel NoCapacityData = new(null, null, null, null);

    private static IManifestRepository ARepositoryHolding(ManifestReadModel? manifest)
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(manifest);
        // Default to "nobody has measured anything" so weight tests that do not care about
        // cargo capacity are not tripped up by an unconfigured mock returning null.
        repository.GetVehicleCargoCapacityAsync(Id, Arg.Any<CancellationToken>()).Returns(NoCapacityData);
        return repository;
    }

    [Fact]
    public async Task Reports_the_crew_travelling_with_its_vehicle()
    {
        // A read of the one crew record, looked up by the manifest's truck-list entry. The
        // manifest used to keep its own driver teams, written through their own endpoint and
        // connected to the convoy's crew by nothing — so the printed document could name people
        // who were not in the vehicle while the insurance covered somebody else.
        var crew = new[]
        {
            new VehicleCrewReadModel(Driver, "Olena", "Kovalenko", JourneyLeg.Uk, CrewRole.Driver),
            new VehicleCrewReadModel(Driver, "Olena", "Kovalenko", JourneyLeg.Border, CrewRole.Driver),
        };
        var repository = ARepositoryHolding(AManifest());
        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.ListCrewAsync(ConvoyId, Vin, null, Arg.Any<CancellationToken>()).Returns(crew);

        var members = await new ListManifestCrewHandler(repository, truckList).HandleAsync(
            new ListManifestCrewQuery(Id), TestContext.Current.CancellationToken);

        members.Should().BeEquivalentTo(crew);
    }

    [Fact]
    public async Task There_is_no_crew_for_a_manifest_that_does_not_exist()
    {
        var repository = ARepositoryHolding(manifest: null);
        var truckList = Substitute.For<IConvoyVehicleRepository>();

        var members = await new ListManifestCrewHandler(repository, truckList).HandleAsync(
            new ListManifestCrewQuery(Id), TestContext.Current.CancellationToken);

        members.Should().BeNull();
        await truckList.DidNotReceive().ListCrewAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<JourneyLeg?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_frozen_manifest_will_not_take_or_release_cargo()
    {
        // Cargo is what the GMR describes, so it is the last thing that may change.
        var repository = ARepositoryHolding(AManifest(frozen: true));

        (await new AddManifestBoxHandler(repository)
                .HandleAsync(new AddManifestBoxCommand(Id, 7), CancellationToken.None))
            .Should().Be(ManifestBoxOutcome.Frozen);

        (await new RemoveManifestBoxHandler(repository)
                .HandleAsync(new RemoveManifestBoxCommand(Id, 7), CancellationToken.None))
            .Should().Be(ManifestBoxOutcome.Frozen);

        await repository.DidNotReceive().AddBoxAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().RemoveBoxAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_the_box_missing_when_there_is_no_such_box()
    {
        var repository = ARepositoryHolding(AManifest());
        repository.AddBoxAsync(Id, 7, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new AddManifestBoxHandler(repository);

        var outcome = await handler.HandleAsync(new AddManifestBoxCommand(Id, 7), CancellationToken.None);

        outcome.Should().Be(ManifestBoxOutcome.BoxNotFound);
    }

    [Fact]
    public async Task Adds_up_the_border_weight_with_its_fixed_allowances_intact()
    {
        // 200 kg for two drivers and their bags, 45 kg fuel. A deliberate border-check estimate
        // that docs/domain/key-concepts.md says explicitly is not a bug.
        var repository = ARepositoryHolding(AManifest());
        repository.ExistsAsync(Id, Arg.Any<CancellationToken>()).Returns(true);
        repository.GetVehicleWeightKgAsync(Id, Arg.Any<CancellationToken>()).Returns(1_400);
        repository.ListBoxesAsync(Id, Arg.Any<CancellationToken>()).Returns(
            new List<ManifestBoxReadModel>
            {
                new(1, 30, Validated: true),
                new(2, 12, Validated: true),
            });
        var handler = new GetManifestWeightHandler(repository);

        var weight = await handler.HandleAsync(new GetManifestWeightQuery(Id), CancellationToken.None);

        weight!.VehicleKg.Should().Be(1_400);
        weight.CargoKg.Should().Be(42);
        weight.CrewAndBagsKg.Should().Be(200);
        weight.FuelKg.Should().Be(45);
        weight.TotalKg.Should().Be(1_687);
        weight.UnvalidatedBoxCount.Should().Be(0);
        weight.MaxCargoWeightKg.Should().BeNull();
        weight.CargoOverweight.Should().BeFalse();
        weight.OversizedBoxIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Flags_the_cargo_as_overweight_against_the_vehicles_stated_capacity()
    {
        var repository = ARepositoryHolding(AManifest());
        repository.ExistsAsync(Id, Arg.Any<CancellationToken>()).Returns(true);
        repository.GetVehicleWeightKgAsync(Id, Arg.Any<CancellationToken>()).Returns(1_400);
        repository.ListBoxesAsync(Id, Arg.Any<CancellationToken>()).Returns(
            new List<ManifestBoxReadModel> { new(1, 30, Validated: true), new(2, 12, Validated: true) });
        repository.GetVehicleCargoCapacityAsync(Id, Arg.Any<CancellationToken>())
            .Returns(new VehicleCargoCapacityReadModel(40m, null, null, null));
        var handler = new GetManifestWeightHandler(repository);

        var weight = await handler.HandleAsync(new GetManifestWeightQuery(Id), CancellationToken.None);

        // 42 kg of cargo against a stated 40 kg maximum.
        weight!.CargoOverweight.Should().BeTrue();
    }

    [Fact]
    public async Task Does_not_flag_cargo_within_the_vehicles_stated_capacity()
    {
        var repository = ARepositoryHolding(AManifest());
        repository.ExistsAsync(Id, Arg.Any<CancellationToken>()).Returns(true);
        repository.GetVehicleWeightKgAsync(Id, Arg.Any<CancellationToken>()).Returns(1_400);
        repository.ListBoxesAsync(Id, Arg.Any<CancellationToken>()).Returns(
            new List<ManifestBoxReadModel> { new(1, 30, Validated: true), new(2, 12, Validated: true) });
        repository.GetVehicleCargoCapacityAsync(Id, Arg.Any<CancellationToken>())
            .Returns(new VehicleCargoCapacityReadModel(100m, null, null, null));
        var handler = new GetManifestWeightHandler(repository);

        var weight = await handler.HandleAsync(new GetManifestWeightQuery(Id), CancellationToken.None);

        weight!.CargoOverweight.Should().BeFalse();
    }

    [Fact]
    public async Task Flags_a_box_that_does_not_fit_the_vehicles_cargo_space()
    {
        var repository = ARepositoryHolding(AManifest());
        repository.ExistsAsync(Id, Arg.Any<CancellationToken>()).Returns(true);
        repository.GetVehicleWeightKgAsync(Id, Arg.Any<CancellationToken>()).Returns(1_400);
        repository.ListBoxesAsync(Id, Arg.Any<CancellationToken>()).Returns(
            new List<ManifestBoxReadModel>
            {
                new(1, 30, Validated: true, WidthCm: 200m, DepthCm: 50m, HeightCm: 50m),
                new(2, 12, Validated: true, WidthCm: 40m, DepthCm: 30m, HeightCm: 20m),
            });
        repository.GetVehicleCargoCapacityAsync(Id, Arg.Any<CancellationToken>())
            .Returns(new VehicleCargoCapacityReadModel(null, 150m, 100m, 100m));
        var handler = new GetManifestWeightHandler(repository);

        var weight = await handler.HandleAsync(new GetManifestWeightQuery(Id), CancellationToken.None);

        // Box 1 is 200x50x50 against a 150x100x100 cargo space — too long in its longest
        // dimension no matter how it is rotated. Box 2 fits easily.
        weight!.OversizedBoxIds.Should().BeEquivalentTo([1]);
    }

    [Fact]
    public async Task A_box_that_fits_only_when_rotated_is_not_flagged()
    {
        // A box measuring 100x40x30 does not fit a 100x100x30 space along the same axes, but it
        // fits once turned on its side — which is why the comparison sorts both sets of
        // dimensions rather than assuming a fixed width/depth/height mapping.
        var repository = ARepositoryHolding(AManifest());
        repository.ExistsAsync(Id, Arg.Any<CancellationToken>()).Returns(true);
        repository.GetVehicleWeightKgAsync(Id, Arg.Any<CancellationToken>()).Returns(1_400);
        repository.ListBoxesAsync(Id, Arg.Any<CancellationToken>()).Returns(
            new List<ManifestBoxReadModel>
            {
                new(1, 30, Validated: true, WidthCm: 100m, DepthCm: 40m, HeightCm: 30m),
            });
        repository.GetVehicleCargoCapacityAsync(Id, Arg.Any<CancellationToken>())
            .Returns(new VehicleCargoCapacityReadModel(null, 100m, 100m, 30m));
        var handler = new GetManifestWeightHandler(repository);

        var weight = await handler.HandleAsync(new GetManifestWeightQuery(Id), CancellationToken.None);

        weight!.OversizedBoxIds.Should().BeEmpty();
    }

    [Fact]
    public async Task A_box_with_no_recorded_dimensions_is_never_flagged_as_oversized()
    {
        var repository = ARepositoryHolding(AManifest());
        repository.ExistsAsync(Id, Arg.Any<CancellationToken>()).Returns(true);
        repository.GetVehicleWeightKgAsync(Id, Arg.Any<CancellationToken>()).Returns(1_400);
        repository.ListBoxesAsync(Id, Arg.Any<CancellationToken>()).Returns(
            new List<ManifestBoxReadModel> { new(1, 30, Validated: false) });
        repository.GetVehicleCargoCapacityAsync(Id, Arg.Any<CancellationToken>())
            .Returns(new VehicleCargoCapacityReadModel(null, 1m, 1m, 1m));
        var handler = new GetManifestWeightHandler(repository);

        var weight = await handler.HandleAsync(new GetManifestWeightQuery(Id), CancellationToken.None);

        weight!.OversizedBoxIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Says_how_many_boxes_nobody_has_weighed_yet()
    {
        // An unvalidated box weighs zero until a Loader says otherwise, so a total containing
        // one is provisional. Reporting the count is what stops it reading as a confirmed figure.
        var repository = ARepositoryHolding(AManifest());
        repository.ExistsAsync(Id, Arg.Any<CancellationToken>()).Returns(true);
        repository.GetVehicleWeightKgAsync(Id, Arg.Any<CancellationToken>()).Returns(1_400);
        repository.ListBoxesAsync(Id, Arg.Any<CancellationToken>()).Returns(
            new List<ManifestBoxReadModel>
            {
                new(1, 30, Validated: true),
                new(2, 0, Validated: false),
            });
        var handler = new GetManifestWeightHandler(repository);

        var weight = await handler.HandleAsync(new GetManifestWeightQuery(Id), CancellationToken.None);

        weight!.UnvalidatedBoxCount.Should().Be(1);
        weight.CargoKg.Should().Be(30);
    }

    [Fact]
    public async Task The_weight_of_an_unknown_manifest_is_nothing_at_all()
    {
        var repository = ARepositoryHolding(null);
        repository.ExistsAsync(Id, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new GetManifestWeightHandler(repository);

        var weight = await handler.HandleAsync(new GetManifestWeightQuery(Id), CancellationToken.None);

        weight.Should().BeNull();
    }
}

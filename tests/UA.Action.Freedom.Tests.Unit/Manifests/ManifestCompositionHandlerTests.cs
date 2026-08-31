using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Manifests;

/// <summary>
/// Composing a manifest — its driver teams, its cargo, and the weight a border check is given.
/// </summary>
public class ManifestCompositionHandlerTests
{
    private const string Id = "MAN-0001";

    private static readonly Guid Primary = new("2b9c1e40-7d8a-4c31-9f52-6a0b8d3e5c11");
    private static readonly Guid Secondary = new("7c1d2e50-8e9b-4d42-a063-7b1c9e4f6d22");

    private static ManifestReadModel AManifest(bool frozen = false) => new(
        Id, "WVWZZZ1JZXW000001", 42, ManifestStatus.Preparing, null, false,
        frozen ? new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc) : null);

    private static PersonReadModel APerson(Guid id, bool isDriver = true) => new(
        id, "Sam", "Whitfield",
        new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        null, isDriver, Committed: true);

    private static IPersonRepository ARosterOfDrivers()
    {
        var people = Substitute.For<IPersonRepository>();
        people.GetByIdAsync(Primary, Arg.Any<CancellationToken>()).Returns(APerson(Primary));
        people.GetByIdAsync(Secondary, Arg.Any<CancellationToken>()).Returns(APerson(Secondary));
        return people;
    }

    private static IManifestRepository ARepositoryHolding(ManifestReadModel? manifest)
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(manifest);
        return repository;
    }

    [Fact]
    public async Task Assigns_a_driver_team_to_a_leg()
    {
        var repository = ARepositoryHolding(AManifest());
        var handler = new SetManifestTeamHandler(repository, ARosterOfDrivers());

        var outcome = await handler.HandleAsync(
            new SetManifestTeamCommand(Id, ManifestLeg.Border, Primary, Secondary), CancellationToken.None);

        outcome.Should().Be(SetManifestTeamOutcome.Set);
        await repository.Received(1).SetTeamAsync(
            Id,
            Arg.Is<ManifestDriverTeamReadModel>(team =>
                team.Leg == ManifestLeg.Border
                && team.PrimaryPersonId == Primary
                && team.SecondaryPersonId == Secondary),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_half_crewed_team_is_allowed_while_the_convoy_is_planned()
    {
        var repository = ARepositoryHolding(AManifest());
        var handler = new SetManifestTeamHandler(repository, ARosterOfDrivers());

        var outcome = await handler.HandleAsync(
            new SetManifestTeamCommand(Id, ManifestLeg.Uk, Primary, null), CancellationToken.None);

        outcome.Should().Be(SetManifestTeamOutcome.Set);
    }

    [Fact]
    public async Task Refuses_a_volunteer_who_never_volunteered_to_drive()
    {
        // Being on the roster is not the same as having agreed to drive a leg to Ukraine.
        var repository = ARepositoryHolding(AManifest());
        var people = Substitute.For<IPersonRepository>();
        people.GetByIdAsync(Primary, Arg.Any<CancellationToken>()).Returns(APerson(Primary, isDriver: false));
        var handler = new SetManifestTeamHandler(repository, people);

        var outcome = await handler.HandleAsync(
            new SetManifestTeamCommand(Id, ManifestLeg.Uk, Primary, null), CancellationToken.None);

        outcome.Should().Be(SetManifestTeamOutcome.DriverIsNotADriver);
        await repository.DidNotReceive().SetTeamAsync(
            Arg.Any<string>(), Arg.Any<ManifestDriverTeamReadModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_a_driver_who_is_not_on_the_roster_at_all()
    {
        var repository = ARepositoryHolding(AManifest());
        var people = Substitute.For<IPersonRepository>();
        people.GetByIdAsync(Primary, Arg.Any<CancellationToken>()).Returns((PersonReadModel?)null);
        var handler = new SetManifestTeamHandler(repository, people);

        var outcome = await handler.HandleAsync(
            new SetManifestTeamCommand(Id, ManifestLeg.Uk, Primary, null), CancellationToken.None);

        outcome.Should().Be(SetManifestTeamOutcome.NoSuchDriver);
    }

    [Fact]
    public async Task Refuses_the_same_volunteer_as_both_halves_of_a_pair()
    {
        // It would look crewed while leaving somebody driving to Ukraine alone.
        var repository = ARepositoryHolding(AManifest());
        var handler = new SetManifestTeamHandler(repository, ARosterOfDrivers());

        var outcome = await handler.HandleAsync(
            new SetManifestTeamCommand(Id, ManifestLeg.Uk, Primary, Primary), CancellationToken.None);

        outcome.Should().Be(SetManifestTeamOutcome.SameDriverTwice);
    }

    [Fact]
    public async Task A_frozen_manifest_will_not_take_a_new_driver_team()
    {
        var repository = ARepositoryHolding(AManifest(frozen: true));
        var handler = new SetManifestTeamHandler(repository, ARosterOfDrivers());

        var outcome = await handler.HandleAsync(
            new SetManifestTeamCommand(Id, ManifestLeg.Uk, Primary, Secondary), CancellationToken.None);

        outcome.Should().Be(SetManifestTeamOutcome.Frozen);
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

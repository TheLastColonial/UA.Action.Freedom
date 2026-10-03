using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// The handover Receiver (ADR 0012, P5): a vehicle on a live convoy may name a registered Receiver, and
/// nothing is written for any other answer.
/// </summary>
public class HandoverReceiverHandlerTests
{
    private const int ConvoyId = 7;
    private const string Vin = "WVWZZZ1JZXW000001";
    private static readonly Guid ReceiverRef = new("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    private sealed record Fixture(
        IConvoyRepository Convoys, IConvoyVehicleRepository TruckList, IReceiverRepository Receivers)
    {
        public SetHandoverReceiverHandler Handler => new(Convoys, TruckList, Receivers);
    }

    private static Fixture AFixture(
        bool arrived = false, bool onConvoy = true, bool withdrawn = false, ReceiverStatus? receiver = ReceiverStatus.Registered)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyId, Arg.Any<CancellationToken>()).Returns(
            new ConvoyReadModel(ConvoyId, Departs, Departs.AddDays(4), null, ArrivedAt: arrived ? Departs.AddDays(4) : null));

        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.GetAsync(ConvoyId, Vin, Arg.Any<CancellationToken>()).Returns(
            onConvoy ? new ConvoyVehicleReadModel(Vin, "AB12CDE", 1_400, 1, 0, withdrawn ? Departs : null) : null);
        truckList.SetHandoverReceiverAsync(ConvoyId, Vin, ReceiverRef, Arg.Any<CancellationToken>()).Returns(true);

        var receivers = Substitute.For<IReceiverRepository>();
        receivers.GetByRefAsync(ReceiverRef, Arg.Any<CancellationToken>()).Returns(
            receiver is { } status ? new ReceiverReadModel(ReceiverRef, "Kharkiv Regional Hospital", "Kharkiv oblast", status) : null);

        return new Fixture(convoys, truckList, receivers);
    }

    private static Task<SetHandoverReceiverOutcome> SetAsync(Fixture fixture, CancellationToken cancellationToken) =>
        fixture.Handler.HandleAsync(new SetHandoverReceiverCommand(ConvoyId, Vin, ReceiverRef), cancellationToken);

    [Fact]
    public async Task A_registered_receiver_is_recorded_against_the_vehicle()
    {
        var fixture = AFixture();

        var outcome = await SetAsync(fixture, TestContext.Current.CancellationToken);

        outcome.Should().Be(SetHandoverReceiverOutcome.Set);
        await fixture.TruckList.Received(1).SetHandoverReceiverAsync(
            ConvoyId, Vin, ReceiverRef, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ReceiverStatus.Pending)]
    [InlineData(ReceiverStatus.Suspended)]
    [InlineData(ReceiverStatus.Expired)]
    public async Task A_receiver_that_is_not_registered_is_refused_and_nothing_is_written(ReceiverStatus status)
    {
        var fixture = AFixture(receiver: status);

        var outcome = await SetAsync(fixture, TestContext.Current.CancellationToken);

        outcome.Should().Be(SetHandoverReceiverOutcome.ReceiverNotRegistered);
        await fixture.TruckList.DidNotReceive().SetHandoverReceiverAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_receiver_that_does_not_exist_is_refused()
    {
        var outcome = await SetAsync(AFixture(receiver: null), TestContext.Current.CancellationToken);

        outcome.Should().Be(SetHandoverReceiverOutcome.ReceiverNotFound);
    }

    [Fact]
    public async Task An_arrived_convoy_is_a_closed_record()
    {
        var outcome = await SetAsync(AFixture(arrived: true), TestContext.Current.CancellationToken);

        outcome.Should().Be(SetHandoverReceiverOutcome.ConvoyArrived);
    }

    [Fact]
    public async Task A_vehicle_that_is_not_on_the_convoy_is_refused()
    {
        var outcome = await SetAsync(AFixture(onConvoy: false), TestContext.Current.CancellationToken);

        outcome.Should().Be(SetHandoverReceiverOutcome.NotOnThisConvoy);
    }

    [Fact]
    public async Task A_withdrawn_vehicle_is_not_handed_over_by_the_convoy()
    {
        var outcome = await SetAsync(AFixture(withdrawn: true), TestContext.Current.CancellationToken);

        outcome.Should().Be(SetHandoverReceiverOutcome.VehicleWithdrawn);
    }

    [Fact]
    public async Task An_unknown_convoy_is_refused()
    {
        var fixture = AFixture();
        fixture.Convoys.GetByIdAsync(ConvoyId, Arg.Any<CancellationToken>()).Returns((ConvoyReadModel?)null);

        var outcome = await SetAsync(fixture, TestContext.Current.CancellationToken);

        outcome.Should().Be(SetHandoverReceiverOutcome.ConvoyNotFound);
    }
}

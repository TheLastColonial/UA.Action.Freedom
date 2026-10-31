using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Tests.Unit.Convoys;

namespace UA.Action.Freedom.Tests.Unit.Boxes;

/// <summary>Accepting a Ukrainian goods list: every delivered box for that Receiver on that vehicle (O9, O24).</summary>
public class AcceptGoodsListHandlerTests
{
    private const int ConvoyId = ConvoyTestData.Id;
    private const string Vin = "WVWZZZ1JZXW000001";
    private static readonly Guid Receiver = new("11111111-1111-1111-1111-111111111111");

    private static ConvoyReadModel ADepartedConvoy() =>
        ConvoyTestData.APublishedConvoy() with { DepartedAt = ConvoyTestData.Start };

    private static (AcceptGoodsListHandler Handler, IBoxOutcomeRepository Outcomes) AHandler(
        ConvoyReadModel? convoy = null, bool vehicleOnConvoy = true, int accepted = 2)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyId, Arg.Any<CancellationToken>()).Returns(convoy);

        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.GetAsync(ConvoyId, Vin, Arg.Any<CancellationToken>())
            .Returns(vehicleOnConvoy ? ConvoyTestData.AVehicle(Vin) : null);

        var outcomes = Substitute.For<IBoxOutcomeRepository>();
        outcomes.AcceptGoodsListAsync(ConvoyId, Vin, Receiver, Arg.Any<CancellationToken>()).Returns(accepted);

        return (new AcceptGoodsListHandler(convoys, truckList, outcomes), outcomes);
    }

    private static Task<AcceptGoodsListResult> Accept(AcceptGoodsListHandler handler) =>
        handler.HandleAsync(new AcceptGoodsListCommand(ConvoyId, Vin, Receiver), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Accepts_the_delivered_boxes_and_says_how_many()
    {
        var (handler, _) = AHandler(ADepartedConvoy(), accepted: 3);

        var result = await Accept(handler);

        result.Outcome.Should().Be(AcceptGoodsListOutcome.Accepted);
        result.BoxesAccepted.Should().Be(3);
    }

    [Fact]
    public async Task Refuses_an_unknown_convoy()
    {
        var (handler, _) = AHandler(convoy: null);

        (await Accept(handler)).Outcome.Should().Be(AcceptGoodsListOutcome.ConvoyNotFound);
    }

    [Fact]
    public async Task Refuses_a_convoy_that_has_not_departed()
    {
        var (handler, outcomes) = AHandler(ConvoyTestData.APublishedConvoy());

        (await Accept(handler)).Outcome.Should().Be(AcceptGoodsListOutcome.ConvoyNotDeparted);
        await outcomes.DidNotReceive().AcceptGoodsListAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_a_vehicle_that_is_not_on_the_convoy()
    {
        var (handler, _) = AHandler(ADepartedConvoy(), vehicleOnConvoy: false);

        (await Accept(handler)).Outcome.Should().Be(AcceptGoodsListOutcome.VehicleNotOnConvoy);
    }

    [Fact]
    public async Task Says_when_there_was_nothing_delivered_to_accept()
    {
        var (handler, _) = AHandler(ADepartedConvoy(), accepted: 0);

        (await Accept(handler)).Outcome.Should().Be(AcceptGoodsListOutcome.NothingToAccept);
    }
}

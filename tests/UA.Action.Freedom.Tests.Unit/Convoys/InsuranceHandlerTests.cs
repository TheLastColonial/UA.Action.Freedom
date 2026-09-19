using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// Recording a vehicle's insurance for a convoy. Whether the vehicle is on the convoy is settled
/// by the write; the handler only tells "no such convoy" apart.
/// </summary>
public class InsuranceHandlerTests
{
    private const string Vin = "WVWZZZ1JZXW000001";

    private static VehicleInsuranceRecord APolicy() => new(
        ConvoyTestData.Id, Vin, "Ukraine Aid Mutual", "POL-1",
        new DateTime(2026, 8, 25), new DateTime(2026, 9, 30), 412.50m, "operator-sub");

    [Theory]
    [InlineData(true, RecordInsuranceOutcome.Recorded)]
    [InlineData(false, RecordInsuranceOutcome.VehicleNotOnConvoy)]
    public async Task Reports_what_the_write_found(bool written, RecordInsuranceOutcome expected)
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(ConvoyTestData.AReadModel());
        repository.RecordInsuranceAsync(APolicy(), Arg.Any<CancellationToken>()).Returns(written);

        var outcome = await new RecordInsuranceHandler(repository).HandleAsync(
            new RecordInsuranceCommand(APolicy()), TestContext.Current.CancellationToken);

        outcome.Should().Be(expected);
    }

    [Fact]
    public async Task Reports_an_unknown_convoy_without_writing()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns((ConvoyReadModel?)null);

        var outcome = await new RecordInsuranceHandler(repository).HandleAsync(
            new RecordInsuranceCommand(APolicy()), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordInsuranceOutcome.ConvoyNotFound);
        await repository.DidNotReceive().RecordInsuranceAsync(Arg.Any<VehicleInsuranceRecord>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, RemoveInsuranceOutcome.Removed)]
    [InlineData(false, RemoveInsuranceOutcome.NotFound)]
    public async Task Removes_insurance(bool removed, RemoveInsuranceOutcome expected)
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.RemoveInsuranceAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>()).Returns(removed);

        var outcome = await new RemoveInsuranceHandler(repository).HandleAsync(
            new RemoveInsuranceCommand(ConvoyTestData.Id, Vin), TestContext.Current.CancellationToken);

        outcome.Should().Be(expected);
    }
}

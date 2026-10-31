using NSubstitute;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Categories;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Declarations;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

internal sealed class FixedClock(DateTime now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(now, DateTimeKind.Utc));
}

/// <summary>
/// Every port the readiness handler reads, as fresh substitutes that answer "nothing there" until a test says otherwise.
/// A test sets only what it is about.
/// </summary>
internal sealed class ReadinessWorld
{
    internal static readonly Guid Receiver = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    public IConvoyRepository Convoys { get; } = Substitute.For<IConvoyRepository>();
    public IConvoyVehicleRepository TruckList { get; } = Substitute.For<IConvoyVehicleRepository>();
    public IConvoyLeaderRepository Leaders { get; } = Substitute.For<IConvoyLeaderRepository>();
    public IAccommodationRepository Accommodation { get; } = Substitute.For<IAccommodationRepository>();
    public IQueryHandler<ListDeclarationsQuery, IReadOnlyList<DeclarationReadModel>?> Declarations { get; } =
        Substitute.For<IQueryHandler<ListDeclarationsQuery, IReadOnlyList<DeclarationReadModel>?>>();
    public IBoxRepository Boxes { get; } = Substitute.For<IBoxRepository>();
    public IReceiverRepository Receivers { get; } = Substitute.For<IReceiverRepository>();
    public IItemCategoryRepository Categories { get; } = Substitute.For<IItemCategoryRepository>();
    public IConvoyBudgetRepository Budget { get; } = Substitute.For<IConvoyBudgetRepository>();
    public IVehicleEquipmentRepository Equipment { get; } = Substitute.For<IVehicleEquipmentRepository>();

    public ReadinessWorld(DateTime? convoyStart = null)
    {
        Convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns(ConvoyTestData.AReadModel() with { Start = convoyStart ?? ConvoyTestData.Start });
        Convoys.GetRouteAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([ConvoyTestData.AStop(1)]);
        TruckList.ListAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([]);
        Leaders.HistoryAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([]);
        Accommodation.ListBookingsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([]);
        Accommodation.ListSelfAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([]);
        Declarations.HandleAsync(Arg.Any<ListDeclarationsQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<DeclarationReadModel>?)[]);
        Categories.ListAsync(Arg.Any<CancellationToken>()).Returns([]);
        Budget.ListLinesAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([]);
        Budget.ListCostsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([]);
        Equipment.ListForConvoyAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([]);
    }

    public GetConvoyReadinessHandler Handler(DateTime? now = null) => new(
        Convoys, TruckList, Leaders, Accommodation, Declarations, Boxes, Receivers, Categories,
        new BudgetPosition(Convoys, TruckList, Budget, Equipment),
        new FixedClock(now ?? ConvoyTestData.Start.AddDays(-3)));

    public Task<ConvoyReadinessReadModel?> ReadAsync(DateTime? now = null) =>
        Handler(now).HandleAsync(new GetConvoyReadinessQuery(ConvoyTestData.Id), TestContext.Current.CancellationToken);

    public ReadinessWorld WithVehicle(ConvoyVehicleReadModel vehicle)
    {
        TruckList.ListAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([vehicle]);
        return this;
    }

    public ReadinessWorld WithPolicy(string vin, VehicleInsuranceReadModel policy)
    {
        TruckList.GetInsuranceAsync(ConvoyTestData.Id, vin, Arg.Any<CancellationToken>()).Returns(policy);
        return this;
    }

    public ReadinessWorld WithReceiver(Guid receiverRef, ReceiverStatus status)
    {
        Receivers.GetByRefAsync(receiverRef, Arg.Any<CancellationToken>()).Returns(new ReceiverReadModel(receiverRef, "Hub", "Lviv", status));
        return this;
    }
}

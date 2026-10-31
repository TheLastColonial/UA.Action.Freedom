using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Categories;
using UA.Action.Freedom.Application.Declarations;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>Whether one vehicle on a convoy is ready to travel, and if not, why.</summary>
public sealed record VehicleReadinessReadModel(
    string Vin,
    string Plate,
    bool Insured,
    bool Ready,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Advisories);

/// <summary>
/// Whether a convoy is ready to travel, and if not, why. <see cref="Requirements"/> is the full list, blocking and
/// advisory; <see cref="Ready"/> is true when no blocking requirement is outstanding, and departure enforces the same.
/// </summary>
public sealed record ConvoyReadinessReadModel(
    bool Ready,
    bool RoutePlanned,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<VehicleReadinessReadModel> Vehicles)
{
    /// <summary>Advice that does not change <see cref="Ready"/>: no budget set, or a budget line over its plan (O37).</summary>
    public IReadOnlyList<string> Advisories { get; init; } = [];

    public IReadOnlyList<Requirement> Requirements { get; init; } = [];
}

public sealed record GetConvoyReadinessQuery(int ConvoyId);

/// <summary>
/// The convoy's readiness, or null when there is no such convoy. The one place the facts are gathered: the departure
/// action calls this too, so what the Dispatcher sees is exactly what the refusal enforces.
/// </summary>
public sealed class GetConvoyReadinessHandler(
    IConvoyRepository convoys,
    IConvoyVehicleRepository truckList,
    IConvoyLeaderRepository leaders,
    IAccommodationRepository accommodation,
    IQueryHandler<ListDeclarationsQuery, IReadOnlyList<DeclarationReadModel>?> declarations,
    IBoxRepository boxes,
    IReceiverRepository receivers,
    IItemCategoryRepository categories,
    BudgetPosition budget,
    TimeProvider clock)
    : IQueryHandler<GetConvoyReadinessQuery, ConvoyReadinessReadModel?>
{
    public async Task<ConvoyReadinessReadModel?> HandleAsync(
        GetConvoyReadinessQuery query, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(query.ConvoyId, cancellationToken);
        if (convoy is null)
        {
            return null;
        }

        var route = await convoys.GetRouteAsync(query.ConvoyId, cancellationToken);
        var entries = await truckList.ListAsync(query.ConvoyId, cancellationToken);
        var overview = await AccommodationOverview.OfAsync(convoys, truckList, accommodation, query.ConvoyId, cancellationToken);
        var leader = await leaders.HistoryAsync(query.ConvoyId, cancellationToken);
        var position = await budget.OfAsync(query.ConvoyId, cancellationToken);
        var categoryById = (await categories.ListAsync(cancellationToken)).ToDictionary(category => category.Id);
        var registered = new Dictionary<Guid, bool>();

        // One set of reads per vehicle still travelling: a convoy is a handful of vans. The rules read nothing themselves.
        var vehicles = new List<VehicleReadinessFacts>();
        foreach (var entry in entries)
        {
            vehicles.Add(entry.Travelling
                ? await VehicleFactsAsync(query.ConvoyId, entry, categoryById, registered, cancellationToken)
                : new VehicleReadinessFacts(entry, null, null, false, [], [], VehicleItemFacts.None));
        }

        var facts = new ConvoyReadinessFacts(
            convoy,
            route.Count > 0,
            leader.Any(assignment => assignment.Until is null),
            new AccommodationFacts(overview.AllCovered, overview.MissingCount, overview.LeftoverBookings.Count),
            vehicles,
            position is null ? [] : BudgetAdvisories.For(position));

        return ConvoyReadiness.Assess(facts, ReadinessClock.AsOf(clock.GetUtcNow().UtcDateTime, convoy.Start));
    }

    private async Task<VehicleReadinessFacts> VehicleFactsAsync(
        int convoyId,
        ConvoyVehicleReadModel entry,
        IReadOnlyDictionary<int, ItemCategoryReadModel> categoryById,
        Dictionary<Guid, bool> registered,
        CancellationToken cancellationToken)
    {
        var policy = await truckList.GetInsuranceAsync(convoyId, entry.Vin, cancellationToken);
        var ferry = await truckList.GetFerryBookingAsync(convoyId, entry.Vin, cancellationToken);
        var allocated = await truckList.ListBoxesAsync(convoyId, entry.Vin, cancellationToken) ?? [];
        var declared = await declarations.HandleAsync(new ListDeclarationsQuery(convoyId, entry.Vin), cancellationToken) ?? [];
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var cargo = new List<CargoBoxFact>();
        var items = new List<BoxItemReadModel>();
        foreach (var allocation in allocated)
        {
            var box = await boxes.GetByIdAsync(allocation.BoxId, cancellationToken);
            cargo.Add(new CargoBoxFact(
                allocation.BoxId,
                allocation.Validated,
                box?.ReceiverRef is { } receiverRef && await IsRegisteredAsync(receiverRef, registered, cancellationToken),
                box?.ReceiverRef));
            items.AddRange(await boxes.ListItemsAsync(allocation.BoxId, cancellationToken));
        }

        var shelfLives = items
            .Select(item => ShelfLife.Assess(item.ExpiresOn, categoryById.GetValueOrDefault(item.CategoryId)?.ShelfLife ?? ShelfLifeRule.None, today))
            .ToList();

        return new VehicleReadinessFacts(
            entry,
            policy,
            ferry,
            entry.HandoverReceiverRef is { } handover && await IsRegisteredAsync(handover, registered, cancellationToken),
            cargo,
            [.. declared.Select(declaration => new DeclarationFact(declaration.Kind, declaration.ReceiverRef, declaration.Status))],
            new VehicleItemFacts(
                items.Count(item => item.ValueGbp is null),
                items.Count(item => item.DonationId is null),
                shelfLives.Count(status => status == ShelfLifeStatus.Expired),
                shelfLives.Count(status => status == ShelfLifeStatus.Short)));
    }

    private async Task<bool> IsRegisteredAsync(Guid receiverRef, Dictionary<Guid, bool> seen, CancellationToken cancellationToken)
    {
        if (seen.TryGetValue(receiverRef, out var known))
        {
            return known;
        }

        var receiver = await receivers.GetByRefAsync(receiverRef, cancellationToken);
        return seen[receiverRef] = receiver is not null && ReceiverRegistration.CanReceive(receiver.Status);
    }
}

/// <summary>
/// The one clock for every date rule in readiness and departure. Insurance must cover the day the convoy sets off: its
/// start, or today if it is going later than planned. A readiness read and a departure agree because both ask this.
/// </summary>
public static class ReadinessClock
{
    public static DateTime AsOf(DateTime now, DateTime convoyStart) => now > convoyStart ? now : convoyStart;
}

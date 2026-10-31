using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Application.Vehicles;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Convoys;

// ---- what the report is built from ---------------------------------------------------------------------------

/// <param name="Outcome">Null while the box has no outcome yet.</param>
/// <param name="ReceiverOrganisation">The Receiver at organisation level. Never an address or a contact.</param>
public sealed record ReportBoxFacts(
    int BoxId, BoxOutcome? Outcome, decimal ValueGbp, string? ReceiverOrganisation, string? ReceiverRegion);

public sealed record ReportVehicleFacts(
    string Vin,
    string Plate,
    decimal? ValueGbp,
    ValueSource? ValueSource,
    bool Withdrawn,
    bool Delivered,
    IReadOnlyList<ReportBoxFacts> Boxes);

public sealed record ConvoyReportFacts(
    int ConvoyId,
    DateTime? ArrivedAt,
    DateTime? ClosedAt,
    BudgetSummaryReadModel? Budget,
    IReadOnlyList<ReportVehicleFacts> Vehicles);

// ---- what the report says ------------------------------------------------------------------------------------

/// <param name="Outcome">A <see cref="BoxOutcome"/> name, or <c>Undecided</c> while the box has none.</param>
public sealed record ReportOutcomeCount(string Outcome, int Boxes, decimal ValueGbp);

/// <summary>
/// A Receiver as the report knows it: organisation and region, aggregated. There is no identifier, address, contact or
/// phone, because the report is an account of the convoy and may be printed and shared (O25, D29).
/// </summary>
public sealed record ReportReceiver(string Organisation, string Region, int Boxes, int BoxesDelivered);

public sealed record ReportVehicle(
    string Vin,
    string Plate,
    decimal? ValueGbp,
    ValueSource? ValueSource,
    bool Withdrawn,
    bool Delivered,
    int Boxes,
    IReadOnlyList<ReportOutcomeCount> ByOutcome,
    decimal DeliveredValueGbp,
    IReadOnlyList<ReportReceiver> Receivers);

public sealed record ConvoyReportTotals(
    int Boxes,
    int BoxesDelivered,
    int BoxesNotDelivered,
    int BoxesUndecided,
    decimal DeliveredValueGbp,
    decimal VehicleValueGbp);

public sealed record ConvoyReportReadModel(
    int ConvoyId,
    DateTime? ArrivedAt,
    DateTime? ClosedAt,
    BudgetSummaryReadModel? Budget,
    IReadOnlyList<ReportVehicle> Vehicles,
    ConvoyReportTotals Totals);

/// <summary>
/// The closing report, computed from facts and nothing else (O10). It is regenerable at any time, so it is never stored.
/// </summary>
/// <remarks>
/// "Delivered" is <see cref="BoxOutcomes.CountsAsDelivered"/>: the one definition, so a seized or undeliverable box is
/// never counted as value delivered here or in any report that uses the same function.
/// </remarks>
public static class ConvoyReports
{
    private const string Undecided = "Undecided";
    private const string Unassigned = "Unassigned";

    public static ConvoyReportReadModel Compose(ConvoyReportFacts facts)
    {
        var vehicles = facts.Vehicles.Select(ComposeVehicle).ToList();

        var boxes = facts.Vehicles.SelectMany(vehicle => vehicle.Boxes).ToList();

        var totals = new ConvoyReportTotals(
            Boxes: boxes.Count,
            BoxesDelivered: boxes.Count(box => box.Outcome is { } outcome && BoxOutcomes.CountsAsDelivered(outcome)),
            BoxesNotDelivered: boxes.Count(box => box.Outcome is { } outcome && !BoxOutcomes.CountsAsDelivered(outcome)),
            BoxesUndecided: boxes.Count(box => box.Outcome is null),
            DeliveredValueGbp: vehicles.Sum(vehicle => vehicle.DeliveredValueGbp),
            VehicleValueGbp: facts.Vehicles
                .Where(vehicle => vehicle.Delivered && !vehicle.Withdrawn)
                .Sum(vehicle => vehicle.ValueGbp ?? 0m));

        return new ConvoyReportReadModel(
            facts.ConvoyId, facts.ArrivedAt, facts.ClosedAt, facts.Budget, vehicles, totals);
    }

    private static ReportVehicle ComposeVehicle(ReportVehicleFacts vehicle) => new(
        vehicle.Vin,
        vehicle.Plate,
        vehicle.ValueGbp,
        vehicle.ValueSource,
        vehicle.Withdrawn,
        vehicle.Delivered,
        vehicle.Boxes.Count,
        vehicle.Boxes
            .GroupBy(box => box.Outcome?.ToString() ?? Undecided)
            .Select(group => new ReportOutcomeCount(group.Key, group.Count(), group.Sum(box => box.ValueGbp)))
            .OrderBy(count => count.Outcome, StringComparer.Ordinal)
            .ToList(),
        BoxOutcomes.DeliveredValue(vehicle.Boxes.Select(box => (box.Outcome, box.ValueGbp))),
        vehicle.Boxes
            .GroupBy(box => (
                Organisation: box.ReceiverOrganisation ?? Unassigned,
                Region: box.ReceiverRegion ?? string.Empty))
            .Select(group => new ReportReceiver(
                group.Key.Organisation,
                group.Key.Region,
                group.Count(),
                group.Count(box => box.Outcome is { } outcome && BoxOutcomes.CountsAsDelivered(outcome))))
            .OrderBy(receiver => receiver.Organisation, StringComparer.Ordinal)
            .ToList());
}

// ---- reading it ----------------------------------------------------------------------------------------------

public sealed record GetConvoyReportQuery(int ConvoyId);

/// <summary>Reads the facts from the existing ports and hands them to <see cref="ConvoyReports.Compose"/>.</summary>
public sealed class GetConvoyReportHandler(
    IConvoyRepository convoys,
    IConvoyVehicleRepository truckList,
    IBoxOutcomeRepository outcomes,
    IBoxRepository boxes,
    IReceiverRepository receivers,
    IVehicleRepository vehicles,
    BudgetPosition budget)
    : IQueryHandler<GetConvoyReportQuery, ConvoyReportReadModel?>
{
    public async Task<ConvoyReportReadModel?> HandleAsync(GetConvoyReportQuery query, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(query.ConvoyId, cancellationToken);

        if (convoy is null)
        {
            return null;
        }

        var currentOutcomes = await outcomes.ListCurrentAsync(query.ConvoyId, cancellationToken);
        var receiverCache = new Dictionary<Guid, ReceiverReadModel?>();
        var vehicleFacts = new List<ReportVehicleFacts>();

        foreach (var entry in await truckList.ListAsync(query.ConvoyId, cancellationToken))
        {
            vehicleFacts.Add(await VehicleFactsAsync(entry, currentOutcomes, receiverCache, query.ConvoyId, cancellationToken));
        }

        return ConvoyReports.Compose(new ConvoyReportFacts(
            convoy.Id,
            convoy.ArrivedAt,
            convoy.ClosedAt,
            await budget.OfAsync(query.ConvoyId, cancellationToken),
            vehicleFacts));
    }

    private async Task<ReportVehicleFacts> VehicleFactsAsync(
        ConvoyVehicleReadModel entry,
        IReadOnlyList<BoxOutcomeReadModel> currentOutcomes,
        Dictionary<Guid, ReceiverReadModel?> receiverCache,
        int convoyId,
        CancellationToken cancellationToken)
    {
        var outcomeOf = currentOutcomes
            .Where(outcome => string.Equals(outcome.Vin, entry.Vin, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(outcome => outcome.BoxId);

        // A seized, undeliverable or returned box has left the vehicle's load, so the cargo list no longer has it; its
        // outcome row still says which vehicle it travelled on.
        var onLoad = (await truckList.ListBoxesAsync(convoyId, entry.Vin, cancellationToken) ?? [])
            .Select(box => box.BoxId);
        var boxIds = onLoad.Concat(outcomeOf.Keys).Distinct().Order().ToList();

        var boxFacts = new List<ReportBoxFacts>();

        foreach (var boxId in boxIds)
        {
            if (await boxes.GetByIdAsync(boxId, cancellationToken) is not { Voided: false } box)
            {
                continue;
            }

            var value = (await boxes.ListItemsAsync(boxId, cancellationToken)).Sum(item => item.ValueGbp ?? 0m);
            var receiver = await ReceiverAsync(box.ReceiverRef, receiverCache, cancellationToken);

            boxFacts.Add(new ReportBoxFacts(
                boxId,
                outcomeOf.TryGetValue(boxId, out var outcome) ? outcome.Outcome : null,
                value,
                receiver?.Organisation,
                receiver?.Region));
        }

        var vehicle = await vehicles.GetByVinAsync(entry.Vin, cancellationToken);

        return new ReportVehicleFacts(
            entry.Vin, entry.Plate, vehicle?.ValueGbp, vehicle?.ValueSource, entry.Withdrawn, entry.Delivered, boxFacts);
    }

    private async Task<ReceiverReadModel?> ReceiverAsync(
        Guid? receiverRef, Dictionary<Guid, ReceiverReadModel?> cache, CancellationToken cancellationToken)
    {
        if (receiverRef is not { } reference)
        {
            return null;
        }

        if (!cache.TryGetValue(reference, out var receiver))
        {
            receiver = await receivers.GetByRefAsync(reference, cancellationToken);
            cache[reference] = receiver;
        }

        return receiver;
    }
}

// ---- closing -------------------------------------------------------------------------------------------------

/// <summary>Close a convoy after it has arrived (O10). The Dispatcher closes; closing does not lock (O25).</summary>
public sealed record CloseConvoyCommand(int ConvoyId);

public enum CloseConvoyOutcome
{
    Closed,
    NotFound,
    NotArrived,
}

public sealed record CloseConvoyResult(CloseConvoyOutcome Outcome, ConvoyReportReadModel? Report = null);

public sealed class CloseConvoyHandler(
    IConvoyRepository convoys,
    IQueryHandler<GetConvoyReportQuery, ConvoyReportReadModel?> report)
    : ICommandHandler<CloseConvoyCommand, CloseConvoyResult>
{
    public async Task<CloseConvoyResult> HandleAsync(CloseConvoyCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(command.ConvoyId, cancellationToken);

        if (convoy is null)
        {
            return new CloseConvoyResult(CloseConvoyOutcome.NotFound);
        }

        if (!convoy.Arrived || !await convoys.CloseAsync(command.ConvoyId, DateTime.UtcNow, cancellationToken))
        {
            return new CloseConvoyResult(CloseConvoyOutcome.NotArrived);
        }

        return new CloseConvoyResult(
            CloseConvoyOutcome.Closed,
            await report.HandleAsync(new GetConvoyReportQuery(command.ConvoyId), cancellationToken));
    }
}

using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Convoys;

public sealed record SetBudgetCommand(int ConvoyId, IReadOnlyList<BudgetLine> Lines);

public enum SetBudgetOutcome
{
    Set,
    ConvoyNotFound
}

/// <summary>Allocate (or change) a convoy's budget. Not required to depart (O37), so nothing reads it as a gate.</summary>
public sealed class SetBudgetHandler(IConvoyRepository convoys, IConvoyBudgetRepository budget)
    : ICommandHandler<SetBudgetCommand, SetBudgetOutcome>
{
    public async Task<SetBudgetOutcome> HandleAsync(SetBudgetCommand command, CancellationToken cancellationToken)
    {
        if (await convoys.GetByIdAsync(command.ConvoyId, cancellationToken) is null)
        {
            return SetBudgetOutcome.ConvoyNotFound;
        }

        await budget.ReplaceLinesAsync(command.ConvoyId, command.Lines, cancellationToken);
        return SetBudgetOutcome.Set;
    }
}

public sealed record GetBudgetQuery(int ConvoyId);

public sealed class GetBudgetHandler(IConvoyRepository convoys, IConvoyBudgetRepository budget)
    : IQueryHandler<GetBudgetQuery, IReadOnlyList<BudgetLineReadModel>?>
{
    public async Task<IReadOnlyList<BudgetLineReadModel>?> HandleAsync(
        GetBudgetQuery query, CancellationToken cancellationToken) =>
        await convoys.GetByIdAsync(query.ConvoyId, cancellationToken) is null
            ? null
            : await budget.ListLinesAsync(query.ConvoyId, cancellationToken);
}

public sealed record AddCostCommand(int ConvoyId, CostType Type, decimal AmountGbp, string? Vin, string? Note);

public enum AddCostOutcome
{
    Created,
    ConvoyNotFound,
    VehicleNotOnConvoy,
    HeldOnBooking
}

public sealed record AddCostResult(AddCostOutcome Outcome, int? Id = null);

/// <summary>
/// Enter a cost against a convoy. Only fuel and other costs are entered: a ferry, hotel or insurance cost lives on its
/// booking or policy, and entering it again as well would count the spend twice.
/// </summary>
public sealed class AddCostHandler(
    IConvoyRepository convoys, IConvoyVehicleRepository truckList, IConvoyBudgetRepository budget)
    : ICommandHandler<AddCostCommand, AddCostResult>
{
    public async Task<AddCostResult> HandleAsync(AddCostCommand command, CancellationToken cancellationToken)
    {
        if (command.Type is not (CostType.Fuel or CostType.Other))
        {
            return new AddCostResult(AddCostOutcome.HeldOnBooking);
        }

        if (await convoys.GetByIdAsync(command.ConvoyId, cancellationToken) is null)
        {
            return new AddCostResult(AddCostOutcome.ConvoyNotFound);
        }

        if (command.Vin is not null && await truckList.GetAsync(command.ConvoyId, command.Vin, cancellationToken) is null)
        {
            return new AddCostResult(AddCostOutcome.VehicleNotOnConvoy);
        }

        var id = await budget.AddCostAsync(
            new ConvoyCostRecord(command.ConvoyId, command.Type, command.AmountGbp, command.Vin, command.Note),
            cancellationToken);

        return new AddCostResult(AddCostOutcome.Created, id);
    }
}

public sealed record DeleteCostCommand(int ConvoyId, int CostId);

public enum DeleteCostOutcome
{
    Deleted,
    NotFound
}

public sealed class DeleteCostHandler(IConvoyBudgetRepository budget)
    : ICommandHandler<DeleteCostCommand, DeleteCostOutcome>
{
    public async Task<DeleteCostOutcome> HandleAsync(DeleteCostCommand command, CancellationToken cancellationToken) =>
        await budget.DeleteCostAsync(command.ConvoyId, command.CostId, cancellationToken)
            ? DeleteCostOutcome.Deleted
            : DeleteCostOutcome.NotFound;
}

public sealed record ListCostsQuery(int ConvoyId);

public sealed class ListCostsHandler(IConvoyRepository convoys, IConvoyBudgetRepository budget)
    : IQueryHandler<ListCostsQuery, IReadOnlyList<ConvoyCostReadModel>?>
{
    public async Task<IReadOnlyList<ConvoyCostReadModel>?> HandleAsync(
        ListCostsQuery query, CancellationToken cancellationToken) =>
        await convoys.GetByIdAsync(query.ConvoyId, cancellationToken) is null
            ? null
            : await budget.ListCostsAsync(query.ConvoyId, cancellationToken);
}

public sealed record BudgetSummaryLine(CostType Type, decimal? PlannedGbp, decimal ActualGbp, bool OverBudget);

public sealed record BudgetSummaryReadModel(
    bool BudgetSet,
    IReadOnlyList<BudgetSummaryLine> Lines,
    decimal PlannedTotalGbp,
    decimal ActualTotalGbp,
    bool AnyOverBudget);

/// <summary>
/// Reads a convoy's budget beside everything spent against it: the costs entered, and the costs already held on the
/// ferry bookings and insurance policies of its vehicles, which are read rather than entered twice.
/// </summary>
public sealed class BudgetPosition(
    IConvoyRepository convoys, IConvoyVehicleRepository truckList, IConvoyBudgetRepository budget)
{
    /// <summary>The position, or null when there is no such convoy.</summary>
    public async Task<BudgetSummaryReadModel?> OfAsync(int convoyId, CancellationToken cancellationToken)
    {
        if (await convoys.GetByIdAsync(convoyId, cancellationToken) is null)
        {
            return null;
        }

        var lines = (await budget.ListLinesAsync(convoyId, cancellationToken))
            .Select(line => new BudgetLine(line.Type, line.PlannedGbp))
            .ToList();

        var entered = (await budget.ListCostsAsync(convoyId, cancellationToken))
            .Select(cost => new ActualCost(cost.Type, cost.AmountGbp, cost.Vin, cost.Note));

        var actuals = entered.Concat(await DerivedAsync(convoyId, cancellationToken)).ToList();
        var comparison = Budget.Compare(lines, actuals);

        return new BudgetSummaryReadModel(
            Budget.IsSet(lines),
            comparison.Select(line => new BudgetSummaryLine(line.Type, line.PlannedGbp, line.ActualGbp, line.OverBudget)).ToList(),
            lines.Sum(line => line.PlannedGbp),
            comparison.Sum(line => line.ActualGbp),
            comparison.Any(line => line.OverBudget));
    }

    private async Task<IReadOnlyList<ActualCost>> DerivedAsync(int convoyId, CancellationToken cancellationToken)
    {
        // One read per vehicle, as readiness does: a convoy is a handful of vans.
        var derived = new List<ActualCost>();
        foreach (var vehicle in await truckList.ListAsync(convoyId, cancellationToken))
        {
            var ferry = await truckList.GetFerryBookingAsync(convoyId, vehicle.Vin, cancellationToken);
            if (ferry?.CostGbp is { } ferryCost)
            {
                derived.Add(new ActualCost(CostType.Ferry, ferryCost, vehicle.Vin, Derived: true));
            }

            var policy = await truckList.GetInsuranceAsync(convoyId, vehicle.Vin, cancellationToken);
            if (policy?.CostGbp is { } insuranceCost)
            {
                derived.Add(new ActualCost(CostType.Insurance, insuranceCost, vehicle.Vin, Derived: true));
            }
        }

        return derived;
    }
}

public sealed record GetBudgetSummaryQuery(int ConvoyId);

public sealed class GetBudgetSummaryHandler(
    IConvoyRepository convoys, IConvoyVehicleRepository truckList, IConvoyBudgetRepository budget)
    : IQueryHandler<GetBudgetSummaryQuery, BudgetSummaryReadModel?>
{
    public Task<BudgetSummaryReadModel?> HandleAsync(GetBudgetSummaryQuery query, CancellationToken cancellationToken) =>
        new BudgetPosition(convoys, truckList, budget).OfAsync(query.ConvoyId, cancellationToken);
}

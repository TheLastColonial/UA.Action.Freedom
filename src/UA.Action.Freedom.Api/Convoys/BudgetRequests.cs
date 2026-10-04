using FluentValidation;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Api.Convoys;

public sealed record BudgetLineRequest(CostType Type, decimal PlannedGbp);

/// <summary>Body of <c>PUT /convoys/{id}/budget</c>: the whole budget. A cost type left out has no line afterwards.</summary>
public sealed record SetBudgetRequest(IReadOnlyList<BudgetLineRequest> Lines)
{
    public SetBudgetCommand ToCommand(int convoyId) =>
        new(convoyId, Lines.Select(line => new BudgetLine(line.Type, line.PlannedGbp)).ToList());
}

/// <summary>
/// Body of <c>POST /convoys/{id}/costs</c>. Who entered it comes from the caller's linked login. Only Fuel and Other
/// are entered: the other types are read from their booking or policy.
/// </summary>
public sealed record AddCostRequest(CostType Type, decimal AmountGbp, string? Vin = null, string? Note = null)
{
    public AddCostCommand ToCommand(int convoyId) => new(convoyId, Type, AmountGbp, Vin, Note);
}

public sealed class SetBudgetRequestValidator : AbstractValidator<SetBudgetRequest>
{
    /// <summary>Mirrors <c>decimal(10,2)</c>.</summary>
    internal const decimal MaxAmountGbp = 99_999_999.99m;

    public SetBudgetRequestValidator()
    {
        RuleFor(r => r.Lines).NotNull();
        RuleFor(r => r.Lines)
            .Must(lines => lines.Select(line => line.Type).Distinct().Count() == lines.Count)
            .When(r => r.Lines is not null)
            .WithMessage("A budget has at most one line for each cost type.");
        RuleForEach(r => r.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Type).IsInEnum();
            line.RuleFor(l => l.PlannedGbp).InclusiveBetween(0m, MaxAmountGbp).PrecisionScale(10, 2, true);
        });
    }
}

public sealed class AddCostRequestValidator : AbstractValidator<AddCostRequest>
{
    public AddCostRequestValidator()
    {
        RuleFor(r => r.Type).IsInEnum();
        RuleFor(r => r.AmountGbp).GreaterThan(0m).LessThanOrEqualTo(SetBudgetRequestValidator.MaxAmountGbp)
            .PrecisionScale(10, 2, true);
        RuleFor(r => r.Vin).MaximumLength(32);
        RuleFor(r => r.Note).MaximumLength(500);
    }
}

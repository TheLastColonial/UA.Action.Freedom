using FluentValidation;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Api.Convoys;

/// <summary>Body of <c>POST /equipment-items</c>: a catalogue entry. The unit cost is optional.</summary>
public sealed record AddEquipmentItemRequest(string Name, decimal? UnitCostGbp = null)
{
    public AddEquipmentItemCommand ToCommand() => new(Name.Trim(), UnitCostGbp);
}

public sealed record VehicleEquipmentLineRequest(int EquipmentItemId, int Quantity, decimal? CostGbp = null);

/// <summary>Body of <c>PUT /convoys/{id}/vehicles/{vin}/equipment</c>: everything on the vehicle. An item left out is removed.</summary>
public sealed record SetVehicleEquipmentRequest(IReadOnlyList<VehicleEquipmentLineRequest> Lines)
{
    public SetVehicleEquipmentCommand ToCommand(int convoyId, string vin) => new(
        convoyId,
        vin,
        Lines.Select(line => new VehicleEquipmentLine(line.EquipmentItemId, line.Quantity, line.CostGbp)).ToList());
}

public sealed class AddEquipmentItemRequestValidator : AbstractValidator<AddEquipmentItemRequest>
{
    public AddEquipmentItemRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.UnitCostGbp).InclusiveBetween(0m, SetBudgetRequestValidator.MaxAmountGbp).PrecisionScale(10, 2, true)
            .When(r => r.UnitCostGbp is not null);
    }
}

public sealed class SetVehicleEquipmentRequestValidator : AbstractValidator<SetVehicleEquipmentRequest>
{
    public SetVehicleEquipmentRequestValidator()
    {
        RuleFor(r => r.Lines).NotNull();
        RuleFor(r => r.Lines)
            .Must(lines => lines.Select(line => line.EquipmentItemId).Distinct().Count() == lines.Count)
            .When(r => r.Lines is not null)
            .WithMessage("An item appears once; put the quantity on one line.");
        RuleForEach(r => r.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Quantity).GreaterThanOrEqualTo(1);
            line.RuleFor(l => l.CostGbp).InclusiveBetween(0m, SetBudgetRequestValidator.MaxAmountGbp).PrecisionScale(10, 2, true)
                .When(l => l.CostGbp is not null);
        });
    }
}

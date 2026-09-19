using FluentValidation;

namespace UA.Action.Freedom.Api.Vehicles;

/// <summary>
/// Shape checks for the vehicle write bodies — enough to keep obviously bad input out of the
/// database. Column widths mirror <c>dbo.Vehicle</c>; VIN is 17 characters in the modern
/// standard but the column allows 32 for older and non-standard numbers.
/// </summary>
public sealed class CreateVehicleRequestValidator : AbstractValidator<CreateVehicleRequest>
{
    public CreateVehicleRequestValidator()
    {
        RuleFor(r => r.Vin).NotEmpty().MaximumLength(32);
        RuleFor(r => r.Plate).NotEmpty().MaximumLength(16);
        RuleFor(r => r.Brand).MaximumLength(64);
        RuleFor(r => r.Model).MaximumLength(64);
        RuleFor(r => r.Colour).MaximumLength(32);
        RuleFor(r => r.Notes).MaximumLength(1000);
        RuleFor(r => r.PurchaserName).MaximumLength(200);
        RuleFor(r => r.Year).InclusiveBetween(1950, 2100);
        RuleFor(r => r.Mileage).GreaterThanOrEqualTo(0).When(r => r.Mileage is not null);
        RuleFor(r => r.WeightKg).GreaterThanOrEqualTo(0);
        RuleFor(r => r.MaxCargoWeightKg).GreaterThanOrEqualTo(0).When(r => r.MaxCargoWeightKg is not null);
        RuleFor(r => r.CargoWidthCm).GreaterThanOrEqualTo(0).When(r => r.CargoWidthCm is not null);
        RuleFor(r => r.CargoDepthCm).GreaterThanOrEqualTo(0).When(r => r.CargoDepthCm is not null);
        RuleFor(r => r.CargoHeightCm).GreaterThanOrEqualTo(0).When(r => r.CargoHeightCm is not null);
        RuleFor(r => r.Transmission).IsInEnum();
        RuleFor(r => r.Fuel).IsInEnum();
    }
}

public sealed class RecordInspectionRequestValidator : AbstractValidator<RecordInspectionRequest>
{
    public RecordInspectionRequestValidator()
    {
        RuleFor(r => r.Status).IsInEnum();
        RuleFor(r => r.Notes).MaximumLength(2000);
    }
}

public sealed class UpdateVehicleRequestValidator : AbstractValidator<UpdateVehicleRequest>
{
    public UpdateVehicleRequestValidator()
    {
        RuleFor(r => r.Plate).NotEmpty().MaximumLength(16);
        RuleFor(r => r.Brand).MaximumLength(64);
        RuleFor(r => r.Model).MaximumLength(64);
        RuleFor(r => r.Colour).MaximumLength(32);
        RuleFor(r => r.Notes).MaximumLength(1000);
        RuleFor(r => r.PurchaserName).MaximumLength(200);
        RuleFor(r => r.Year).InclusiveBetween(1950, 2100);
        RuleFor(r => r.Mileage).GreaterThanOrEqualTo(0).When(r => r.Mileage is not null);
        RuleFor(r => r.WeightKg).GreaterThanOrEqualTo(0);
        RuleFor(r => r.MaxCargoWeightKg).GreaterThanOrEqualTo(0).When(r => r.MaxCargoWeightKg is not null);
        RuleFor(r => r.CargoWidthCm).GreaterThanOrEqualTo(0).When(r => r.CargoWidthCm is not null);
        RuleFor(r => r.CargoDepthCm).GreaterThanOrEqualTo(0).When(r => r.CargoDepthCm is not null);
        RuleFor(r => r.CargoHeightCm).GreaterThanOrEqualTo(0).When(r => r.CargoHeightCm is not null);
        RuleFor(r => r.Transmission).IsInEnum();
        RuleFor(r => r.Fuel).IsInEnum();
    }
}

using FluentValidation;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Api.Boxes;

/// <summary>Body of <c>POST /boxes</c>. Weight is not settable — validation confirms it.</summary>
public sealed record CreateBoxRequest(Guid? ReceiverRef, int? LocationId)
{
    public CreateBoxCommand ToCommand() => new(ReceiverRef, LocationId);
}

/// <summary>Body of <c>PUT /boxes/{id}</c>. The route supplies the identifier.</summary>
public sealed record UpdateBoxRequest(Guid? ReceiverRef, int? LocationId)
{
    public UpdateBoxCommand ToCommand(int id) => new(id, ReceiverRef, LocationId);
}

/// <summary>
/// Body of <c>PUT /boxes/{id}/bay</c> — a Loader placing the box in a bay. Who placed it is the
/// caller's linked person, never a field here: an <c>assignedByPersonId</c> in the body is ignored.
/// </summary>
public sealed record AssignBoxBayRequest(int BayId)
{
    public AssignBoxBayCommand ToCommand(int boxId, Guid assignedByPersonId) => new(boxId, BayId, assignedByPersonId);
}

/// <summary>
/// Body of <c>POST /boxes/{id}/validate</c> — the Loader's confirmation of contents and weight.
/// Who vouched for the box is the caller's linked person, never a field here: a
/// <c>validatedByPersonId</c> in the body is ignored.
/// </summary>
public sealed record ValidateBoxRequest(
    int WeightKg,
    decimal? WidthCm = null, decimal? DepthCm = null, decimal? HeightCm = null)
{
    public ValidateBoxCommand ToCommand(int id, Guid validatedByPersonId) =>
        new(id, validatedByPersonId, WeightKg, WidthCm, DepthCm, HeightCm);
}

/// <summary>Body of <c>POST /boxes/{id}/items</c>.</summary>
public sealed record AddBoxItemRequest(
    string Description,
    Dictionary<string, string>? Properties,
    int CategoryId,
    string? CommodityCode = null,
    int? Quantity = null,
    decimal? ValueGbp = null,
    ValueSource? ValueSource = null,
    DateOnly? ExpiresOn = null)
{
    public AddBoxItemCommand ToCommand(int boxId) =>
        new(boxId, Description, Properties ?? [], CategoryId, CommodityCode, Quantity, ValueGbp, ValueSource, ExpiresOn);
}

/// <summary>Written out for each body rather than shared, matching the vehicle and volunteer validators.</summary>
public sealed class CreateBoxRequestValidator : AbstractValidator<CreateBoxRequest>
{
    public CreateBoxRequestValidator()
    {
        RuleFor(r => r.LocationId).GreaterThan(0).When(r => r.LocationId is not null);
    }
}

public sealed class UpdateBoxRequestValidator : AbstractValidator<UpdateBoxRequest>
{
    public UpdateBoxRequestValidator()
    {
        RuleFor(r => r.LocationId).GreaterThan(0).When(r => r.LocationId is not null);
    }
}

/// <summary>Body of <c>PUT /boxes/{id}/bay</c>.</summary>
public sealed class AssignBoxBayRequestValidator : AbstractValidator<AssignBoxBayRequest>
{
    public AssignBoxBayRequestValidator()
    {
        RuleFor(r => r.BayId).GreaterThan(0);
    }
}

public sealed class ValidateBoxRequestValidator : AbstractValidator<ValidateBoxRequest>
{
    /// <summary>
    /// A box a volunteer can carry. The upper bound is a typo guard — a four-digit weight here
    /// would sail through to a border document as a fact somebody had signed for.
    /// </summary>
    private const int MaxBoxWeightKg = 500;

    /// <summary>
    /// A box a volunteer can carry. Like <see cref="MaxBoxWeightKg"/>, a typo guard rather than
    /// a real bound — a box over 10 metres in any dimension is a data-entry mistake.
    /// </summary>
    private const int MaxBoxDimensionCm = 1000;

    public ValidateBoxRequestValidator()
    {
        RuleFor(r => r.WeightKg).InclusiveBetween(1, MaxBoxWeightKg)
            .WithMessage($"'Weight Kg' must be between 1 and {MaxBoxWeightKg}.");
        RuleFor(r => r.WidthCm).InclusiveBetween(1, MaxBoxDimensionCm).When(r => r.WidthCm is not null)
            .WithMessage($"'Width Cm' must be between 1 and {MaxBoxDimensionCm}.");
        RuleFor(r => r.DepthCm).InclusiveBetween(1, MaxBoxDimensionCm).When(r => r.DepthCm is not null)
            .WithMessage($"'Depth Cm' must be between 1 and {MaxBoxDimensionCm}.");
        RuleFor(r => r.HeightCm).InclusiveBetween(1, MaxBoxDimensionCm).When(r => r.HeightCm is not null)
            .WithMessage($"'Height Cm' must be between 1 and {MaxBoxDimensionCm}.");
    }
}

public sealed class AddBoxItemRequestValidator : AbstractValidator<AddBoxItemRequest>
{
    private const int MaxProperties = 50;

    public AddBoxItemRequestValidator()
    {
        RuleFor(r => r.Description).NotEmpty().MaximumLength(400);
        RuleFor(r => r.CategoryId).GreaterThan(0);
        RuleFor(r => r.CommodityCode!).Matches(Categories.CommodityCodes.Pattern)
            .WithMessage("'Commodity Code' must be 6 to 10 digits.")
            .When(r => r.CommodityCode is not null);
        RuleFor(r => r.Quantity).GreaterThanOrEqualTo(1).When(r => r.Quantity is not null);
        RuleFor(r => r.ValueGbp).GreaterThanOrEqualTo(0).PrecisionScale(12, 2, ignoreTrailingZeros: true)
            .When(r => r.ValueGbp is not null);
        RuleFor(r => r.ValueSource)
            .NotNull().WithMessage("A value needs its source: Donor or Estimate.")
            .When(r => r.ValueGbp is not null);
        RuleFor(r => r.ValueGbp)
            .NotNull().WithMessage("A value source needs the value it describes.")
            .When(r => r.ValueSource is not null);
        RuleFor(r => r.ValueSource)
            .Must(source => source is ValueSource.Donor or ValueSource.Estimate)
            .WithMessage("An item's value comes from the Donor or is an Estimate.")
            .When(r => r.ValueSource is not null);
        RuleFor(r => r.Properties)
            .Must(properties => properties is null || properties.Count <= MaxProperties)
            .WithMessage($"An item may carry at most {MaxProperties} properties.");
        RuleFor(r => r.Properties)
            .Must(properties => properties is null || properties.Keys.All(key => key.Length <= 100))
            .WithMessage("Property names must be 100 characters or fewer.");
    }
}

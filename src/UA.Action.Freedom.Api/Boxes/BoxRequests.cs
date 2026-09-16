using FluentValidation;
using UA.Action.Freedom.Application.Boxes;

namespace UA.Action.Freedom.Api.Boxes;

/// <summary>Body of <c>POST /boxes</c>. Weight is not settable — validation confirms it.</summary>
public sealed record CreateBoxRequest(
    Guid? ReceiverRef, string? House, string? Street, string? City, string? Country, string? Postcode)
{
    public CreateBoxCommand ToCommand() => new(ReceiverRef, House, Street, City, Country, Postcode);
}

/// <summary>Body of <c>PUT /boxes/{id}</c>. The route supplies the identifier.</summary>
public sealed record UpdateBoxRequest(
    Guid? ReceiverRef, string? House, string? Street, string? City, string? Country, string? Postcode)
{
    public UpdateBoxCommand ToCommand(int id) => new(id, ReceiverRef, House, Street, City, Country, Postcode);
}

/// <summary>
/// Body of <c>POST /boxes/{id}/validate</c> — the Loader's confirmation of contents and weight.
/// </summary>
public sealed record ValidateBoxRequest(
    Guid ValidatedByPersonId, int WeightKg,
    decimal? WidthCm = null, decimal? DepthCm = null, decimal? HeightCm = null)
{
    public ValidateBoxCommand ToCommand(int id) => new(id, ValidatedByPersonId, WeightKg, WidthCm, DepthCm, HeightCm);
}

/// <summary>Body of <c>POST /boxes/{id}/items</c>.</summary>
public sealed record AddBoxItemRequest(string Description, Dictionary<string, string>? Properties)
{
    public AddBoxItemCommand ToCommand(int boxId) => new(boxId, Description, Properties ?? []);
}

/// <summary>
/// Column widths for the box's current location, mirroring <c>dbo.Box</c>. Written out for each
/// body rather than shared, matching the vehicle and volunteer validators.
/// </summary>
public sealed class CreateBoxRequestValidator : AbstractValidator<CreateBoxRequest>
{
    public CreateBoxRequestValidator()
    {
        RuleFor(r => r.House).MaximumLength(100);
        RuleFor(r => r.Street).MaximumLength(200);
        RuleFor(r => r.City).MaximumLength(100);
        RuleFor(r => r.Country).MaximumLength(100);
        RuleFor(r => r.Postcode).MaximumLength(20);
    }
}

public sealed class UpdateBoxRequestValidator : AbstractValidator<UpdateBoxRequest>
{
    public UpdateBoxRequestValidator()
    {
        RuleFor(r => r.House).MaximumLength(100);
        RuleFor(r => r.Street).MaximumLength(200);
        RuleFor(r => r.City).MaximumLength(100);
        RuleFor(r => r.Country).MaximumLength(100);
        RuleFor(r => r.Postcode).MaximumLength(20);
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
        RuleFor(r => r.ValidatedByPersonId).NotEmpty()
            .WithMessage("'Validated By Person Id' must name the volunteer who checked the box.");
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
        RuleFor(r => r.Properties)
            .Must(properties => properties is null || properties.Count <= MaxProperties)
            .WithMessage($"An item may carry at most {MaxProperties} properties.");
        RuleFor(r => r.Properties)
            .Must(properties => properties is null || properties.Keys.All(key => key.Length <= 100))
            .WithMessage("Property names must be 100 characters or fewer.");
    }
}

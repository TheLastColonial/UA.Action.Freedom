using FluentValidation;
using UA.Action.Freedom.Application.Categories;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Api.Categories;

/// <summary>
/// Body of <c>POST /categories</c>. Whether a category is fixed is not a field: only the seeded list is, and no
/// request can make another.
/// </summary>
public sealed record CreateCategoryRequest(
    string NameEn, string? NameUk, int? HazardClass, bool IsSensitive, bool IsNotCarried, int? WarnWithinDays)
{
    public CreateCategoryCommand ToCommand() =>
        new(NameEn, NameUk ?? string.Empty, HazardClass, IsSensitive, IsNotCarried, WarnWithinDays);
}

/// <summary>Body of <c>PUT /categories/{id}</c>. The route supplies the identifier.</summary>
public sealed record UpdateCategoryRequest(
    string NameEn, string? NameUk, int? HazardClass, bool IsSensitive, bool IsNotCarried, int? WarnWithinDays)
{
    public UpdateCategoryCommand ToCommand(int id) =>
        new(id, NameEn, NameUk ?? string.Empty, HazardClass, IsSensitive, IsNotCarried, WarnWithinDays);
}

/// <summary>Body of <c>PUT /categories/{id}/codes/{authority}</c>. A <see langword="null"/> code clears the mapping.</summary>
public sealed record SetCategoryCodeRequest(string? Code)
{
    public SetCategoryCodeCommand ToCommand(int id, CustomsAuthority authority) => new(id, authority, Code);
}

/// <summary>Column widths mirror <c>dbo.ItemCategory</c>.</summary>
public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(r => r.NameEn).NotEmpty().MaximumLength(100);
        RuleFor(r => r.NameUk).MaximumLength(100);
        RuleFor(r => r.HazardClass).InclusiveBetween(1, 9).When(r => r.HazardClass is not null);
        RuleFor(r => r.WarnWithinDays).GreaterThanOrEqualTo(0).When(r => r.WarnWithinDays is not null);
    }
}

public sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(r => r.NameEn).NotEmpty().MaximumLength(100);
        RuleFor(r => r.NameUk).MaximumLength(100);
        RuleFor(r => r.HazardClass).InclusiveBetween(1, 9).When(r => r.HazardClass is not null);
        RuleFor(r => r.WarnWithinDays).GreaterThanOrEqualTo(0).When(r => r.WarnWithinDays is not null);
    }
}

public sealed class SetCategoryCodeRequestValidator : AbstractValidator<SetCategoryCodeRequest>
{
    public SetCategoryCodeRequestValidator()
    {
        RuleFor(r => r.Code!).Matches(CommodityCodes.Pattern)
            .WithMessage("'Code' must be 6 to 10 digits.")
            .When(r => r.Code is not null);
    }
}

/// <summary>The shape of a commodity code: at least six digits, as ICS2 requires per goods item, and no more than the column holds.</summary>
public static class CommodityCodes
{
    public const string Pattern = @"^\d{6,10}$";
}

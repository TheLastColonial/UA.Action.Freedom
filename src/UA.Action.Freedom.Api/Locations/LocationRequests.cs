using FluentValidation;
using UA.Action.Freedom.Application.Locations;

namespace UA.Action.Freedom.Api.Locations;

/// <summary>
/// Body of <c>POST /locations</c>. <paramref name="IsRegisteredHub"/> marks the location as a distribution hub an
/// Administrator has registered; creating a location is Administrator only, so no one else can set it.
/// </summary>
public sealed record CreateLocationRequest(
    string Name, string? House, string? Street, string? City, string? Country, string? Postcode,
    bool IsRegisteredHub = false)
{
    public CreateLocationCommand ToCommand() => new(Name, House, Street, City, Country, Postcode, IsRegisteredHub);
}

/// <summary>Body of <c>PUT /locations/{id}</c>. The route supplies the identifier.</summary>
public sealed record UpdateLocationRequest(
    string Name, string? House, string? Street, string? City, string? Country, string? Postcode,
    bool IsRegisteredHub = false)
{
    public UpdateLocationCommand ToCommand(int id) =>
        new(id, Name, House, Street, City, Country, Postcode, IsRegisteredHub);
}

/// <summary>Body of <c>POST /locations/{id}/bays</c>.</summary>
public sealed record CreateBayRequest(string Code)
{
    public CreateBayCommand ToCommand(int locationId) => new(locationId, Code);
}

/// <summary>Body of <c>PUT /locations/{id}/bays/{bayId}</c>.</summary>
public sealed record UpdateBayRequest(string Code)
{
    public UpdateBayCommand ToCommand(int locationId, int bayId) => new(bayId, locationId, Code);
}

/// <summary>Column widths mirror <c>dbo.Location</c>.</summary>
public sealed class CreateLocationRequestValidator : AbstractValidator<CreateLocationRequest>
{
    public CreateLocationRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.House).MaximumLength(100);
        RuleFor(r => r.Street).MaximumLength(200);
        RuleFor(r => r.City).MaximumLength(100);
        RuleFor(r => r.Country).MaximumLength(100);
        RuleFor(r => r.Postcode).MaximumLength(20);
    }
}

public sealed class UpdateLocationRequestValidator : AbstractValidator<UpdateLocationRequest>
{
    public UpdateLocationRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.House).MaximumLength(100);
        RuleFor(r => r.Street).MaximumLength(200);
        RuleFor(r => r.City).MaximumLength(100);
        RuleFor(r => r.Country).MaximumLength(100);
        RuleFor(r => r.Postcode).MaximumLength(20);
    }
}

/// <summary>Column width mirrors <c>dbo.Bay</c>.</summary>
public sealed class CreateBayRequestValidator : AbstractValidator<CreateBayRequest>
{
    public CreateBayRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().MaximumLength(20);
    }
}

public sealed class UpdateBayRequestValidator : AbstractValidator<UpdateBayRequest>
{
    public UpdateBayRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().MaximumLength(20);
    }
}

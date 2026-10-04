using FluentValidation;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Api.Convoys;

/// <summary>
/// Body of <c>POST /convoys/{id}/accommodation</c> and <c>PUT /convoys/{id}/accommodation/{bookingId}</c>: a stay at a
/// route point for the named crew. Guests are person ids; no name is ever sent or stored.
/// </summary>
public sealed record AccommodationBookingRequest(
    int RoutePointId,
    string Provider,
    DateTime CheckIn,
    DateTime CheckOut,
    IReadOnlyList<Guid> Guests,
    string? Reference = null,
    string? Details = null,
    decimal? CostGbp = null)
{
    public AccommodationBookingRecord ToRecord(int convoyId) =>
        new(convoyId, RoutePointId, Provider.Trim(), Reference, CheckIn, CheckOut, Details, CostGbp, [.. Guests.Distinct()]);
}

/// <summary>Body of <c>POST /convoys/{id}/accommodation/{bookingId}/migrate</c>.</summary>
public sealed record MigrateAccommodationRequest(Guid FromPersonId, Guid ToPersonId);

public sealed class AccommodationBookingRequestValidator : AbstractValidator<AccommodationBookingRequest>
{
    public AccommodationBookingRequestValidator()
    {
        RuleFor(r => r.RoutePointId).GreaterThan(0);
        RuleFor(r => r.Provider).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Reference).MaximumLength(100);
        RuleFor(r => r.Details).MaximumLength(1000);
        RuleFor(r => r.CheckOut).GreaterThanOrEqualTo(r => r.CheckIn)
            .WithMessage("'CheckOut' must not be before 'CheckIn'.");
        RuleFor(r => r.CostGbp!.Value).InclusiveBetween(0m, SetBudgetRequestValidator.MaxAmountGbp).PrecisionScale(10, 2, true)
            .When(r => r.CostGbp is not null);
        RuleFor(r => r.Guests).NotNull().NotEmpty().WithMessage("A booking covers at least one crew member.");
        RuleForEach(r => r.Guests).NotEqual(Guid.Empty);
    }
}

public sealed class MigrateAccommodationRequestValidator : AbstractValidator<MigrateAccommodationRequest>
{
    public MigrateAccommodationRequestValidator()
    {
        RuleFor(r => r.FromPersonId).NotEqual(Guid.Empty);
        RuleFor(r => r.ToPersonId).NotEqual(Guid.Empty).NotEqual(r => r.FromPersonId)
            .WithMessage("A place cannot be migrated to the person who holds it.");
    }
}

using FluentValidation;

namespace UA.Action.Freedom.Api.Convoys;

/// <summary>
/// Shape checks for the convoy write bodies. Column widths mirror <c>dbo.Convoy</c> and
/// <c>dbo.ConvoyRouteStop</c>.
/// </summary>
public sealed class CreateConvoyRequestValidator : AbstractValidator<CreateConvoyRequest>
{
    public CreateConvoyRequestValidator()
    {
        RuleFor(r => r.Start).GreaterThan(ConvoyDates.Earliest);
        RuleFor(r => r.ExpectedEnd).GreaterThan(r => r.Start)
            .WithMessage("'Expected End' must be after 'Start'.");
    }
}

public sealed class UpdateConvoyRequestValidator : AbstractValidator<UpdateConvoyRequest>
{
    public UpdateConvoyRequestValidator()
    {
        RuleFor(r => r.Start).GreaterThan(ConvoyDates.Earliest);
        RuleFor(r => r.ExpectedEnd).GreaterThan(r => r.Start)
            .WithMessage("'Expected End' must be after 'Start'.");
    }
}

/// <summary>
/// A route is replaced whole, so the rules are about the journey as much as the stops.
/// </summary>
public sealed class ReplaceConvoyRouteRequestValidator : AbstractValidator<ReplaceConvoyRouteRequest>
{
    /// <summary>
    /// UK depot to Ukrainian delivery is a handful of stops. A cap keeps a runaway client from
    /// writing an unbounded number of rows in one transaction.
    /// </summary>
    private const int MaxStops = 100;

    public ReplaceConvoyRouteRequestValidator()
    {
        RuleFor(r => r.Stops).NotNull().Must(stops => stops is null || stops.Count <= MaxStops)
            .WithMessage($"A route may have at most {MaxStops} stops.");

        RuleForEach(r => r.Stops).ChildRules(stop =>
        {
            stop.RuleFor(s => s.Postcode).NotEmpty().MaximumLength(20);
            stop.RuleFor(s => s.House).MaximumLength(100);
            stop.RuleFor(s => s.Street).MaximumLength(200);
            stop.RuleFor(s => s.City).MaximumLength(100);
            stop.RuleFor(s => s.Country).MaximumLength(100);
        });
    }
}

internal static class ConvoyDates
{
    /// <summary>
    /// Catches a missing date arriving as <c>default(DateTime)</c> rather than being a real bound.
    /// </summary>
    internal static readonly DateTime Earliest = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}

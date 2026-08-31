using FluentValidation;

namespace UA.Action.Freedom.Api.People;

/// <summary>
/// Shape checks for the volunteer write bodies — enough to keep obviously bad input out of the
/// database. Column widths mirror <c>dbo.Person</c>.
/// </summary>
/// <remarks>
/// Every message names the field and never quotes the value: a validation response is the one
/// place a phone number or a date of birth could otherwise escape into a client log
/// (docs/recommendations.md §4.8).
/// </remarks>
public sealed class CreatePersonRequestValidator : AbstractValidator<CreatePersonRequest>
{
    public CreatePersonRequestValidator()
    {
        RuleFor(r => r.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.LastName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Phone).MaximumLength(50);
        RuleFor(r => r.DateOfBirth).GreaterThan(PersonDates.Earliest)
            .WithMessage("'Date Of Birth' is not a plausible date.");
        RuleFor(r => r.Joined).GreaterThan(PersonDates.Earliest)
            .WithMessage("'Joined' is not a plausible date.");

        // Commitment is a commitment to drive a leg of a convoy. A volunteer who does not drive
        // cannot be committed to one, and letting the two disagree would put someone on the
        // dispatcher's driver shortlist who never agreed to drive.
        RuleFor(r => r.Committed).Must((request, committed) => !committed || request.IsDriver)
            .WithMessage("'Committed' can only be set for a volunteer who drives.");
    }
}

public sealed class UpdatePersonRequestValidator : AbstractValidator<UpdatePersonRequest>
{
    public UpdatePersonRequestValidator()
    {
        RuleFor(r => r.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.LastName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Phone).MaximumLength(50);
        RuleFor(r => r.DateOfBirth).GreaterThan(PersonDates.Earliest)
            .WithMessage("'Date Of Birth' is not a plausible date.");
        RuleFor(r => r.Joined).GreaterThan(PersonDates.Earliest)
            .WithMessage("'Joined' is not a plausible date.");

        RuleFor(r => r.Committed).Must((request, committed) => !committed || request.IsDriver)
            .WithMessage("'Committed' can only be set for a volunteer who drives.");
    }
}

internal static class PersonDates
{
    /// <summary>
    /// Nobody supporting the charity was born, or joined it, before this. It exists to catch a
    /// missing field arriving as <c>default(DateTime)</c> rather than to be a real bound.
    /// </summary>
    internal static readonly DateTime Earliest = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}

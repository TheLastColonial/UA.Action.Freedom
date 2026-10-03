using FluentValidation;
using UA.Action.Freedom.Application.Donations;

namespace UA.Action.Freedom.Api.Donations;

/// <summary>Body of <c>POST /donors</c>. The identifier is minted by the application.</summary>
public sealed record CreateDonorRequest(string Name, string? Email, string? Phone)
{
    public CreateDonorCommand ToCommand() => new(Name, Email, Phone);
}

/// <summary>Body of <c>PUT /donors/{id}</c>. The route supplies the identifier.</summary>
public sealed record UpdateDonorRequest(string Name, string? Email, string? Phone)
{
    public UpdateDonorCommand ToCommand(Guid id) => new(id, Name, Email, Phone);
}

/// <summary>Body of <c>POST /donations</c>.</summary>
public sealed record CreateDonationRequest(Guid DonorId, DateOnly? ReceivedOn, string? Notes)
{
    public CreateDonationCommand ToCommand() => new(DonorId, ReceivedOn ?? default, Notes);
}

/// <summary>Body of <c>PUT /donations/{id}</c>. A donation stays with its donor, so only the date and notes change.</summary>
public sealed record UpdateDonationRequest(DateOnly? ReceivedOn, string? Notes)
{
    public UpdateDonationCommand ToCommand(int id) => new(id, ReceivedOn ?? default, Notes);
}

/// <summary>
/// Shape checks for the donor and donation bodies; column widths mirror <c>dbo.DonorDetail</c> and
/// <c>dbo.Donation</c>. Every message names the field and never quotes the value, because a validation response is
/// the one place a donor's email or phone number could otherwise escape into a client log.
/// </summary>
public sealed class CreateDonorRequestValidator : AbstractValidator<CreateDonorRequest>
{
    public CreateDonorRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Email!).EmailAddress().MaximumLength(254).When(r => r.Email is not null);
        RuleFor(r => r.Phone).MaximumLength(50);
    }
}

public sealed class UpdateDonorRequestValidator : AbstractValidator<UpdateDonorRequest>
{
    public UpdateDonorRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Email!).EmailAddress().MaximumLength(254).When(r => r.Email is not null);
        RuleFor(r => r.Phone).MaximumLength(50);
    }
}

public sealed class CreateDonationRequestValidator : AbstractValidator<CreateDonationRequest>
{
    public CreateDonationRequestValidator()
    {
        RuleFor(r => r.DonorId).NotEmpty();
        RuleFor(r => r.ReceivedOn).NotNull().WithMessage("'Received On' is required.");
        RuleFor(r => r.Notes).MaximumLength(1000);
    }
}

public sealed class UpdateDonationRequestValidator : AbstractValidator<UpdateDonationRequest>
{
    public UpdateDonationRequestValidator()
    {
        RuleFor(r => r.ReceivedOn).NotNull().WithMessage("'Received On' is required.");
        RuleFor(r => r.Notes).MaximumLength(1000);
    }
}

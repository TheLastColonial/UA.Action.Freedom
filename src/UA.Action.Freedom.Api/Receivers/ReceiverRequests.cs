using FluentValidation;
using UA.Action.Freedom.Application.Receivers;

namespace UA.Action.Freedom.Api.Receivers;

/// <summary>Body of <c>POST /receivers</c>. The reference is minted by the application.</summary>
public sealed record CreateReceiverRequest(string Organisation, string Region)
{
    public CreateReceiverCommand ToCommand() => new(Organisation, Region);
}

/// <summary>Body of <c>PUT /receivers/{ref}</c>. The route supplies the reference.</summary>
public sealed record UpdateReceiverRequest(string Organisation, string Region)
{
    public UpdateReceiverCommand ToCommand(Guid receiverRef) => new(receiverRef, Organisation, Region);
}

/// <summary>
/// Body of <c>PUT /receivers/{ref}/detail</c> — the Ukrainian delivery address and contact.
/// </summary>
/// <remarks>
/// Only a Ground Officer can send this, and only a Ground Officer can read it back.
/// </remarks>
public sealed record SetReceiverDetailRequest(
    string ContactName,
    string ContactPhone,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? PostCode,
    DateTime? DeleteAfter)
{
    public SetReceiverDetailCommand ToCommand(Guid receiverRef) => new(
        receiverRef, ContactName, ContactPhone, AddressLine1, AddressLine2, City, PostCode, DeleteAfter);
}

/// <summary>
/// Shape checks for the receiver write bodies. Column widths mirror <c>dbo.Receiver</c>.
/// </summary>
public sealed class CreateReceiverRequestValidator : AbstractValidator<CreateReceiverRequest>
{
    public CreateReceiverRequestValidator()
    {
        RuleFor(r => r.Organisation).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Region).NotEmpty().MaximumLength(100);
    }
}

public sealed class UpdateReceiverRequestValidator : AbstractValidator<UpdateReceiverRequest>
{
    public UpdateReceiverRequestValidator()
    {
        RuleFor(r => r.Organisation).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Region).NotEmpty().MaximumLength(100);
    }
}

/// <summary>
/// Shape checks for delivery detail. Column widths mirror <c>sensitive.ReceiverDetail</c>.
/// </summary>
/// <remarks>
/// Messages name fields and never quote values. A validation response for this body would
/// otherwise put a Ukrainian street address into whatever logs the client keeps — the exact
/// disclosure the segregation exists to prevent.
/// </remarks>
public sealed class SetReceiverDetailRequestValidator : AbstractValidator<SetReceiverDetailRequest>
{
    public SetReceiverDetailRequestValidator()
    {
        RuleFor(r => r.ContactName).NotEmpty().MaximumLength(200);
        RuleFor(r => r.ContactPhone).NotEmpty().MaximumLength(50);
        RuleFor(r => r.AddressLine1).NotEmpty().MaximumLength(200);
        RuleFor(r => r.AddressLine2).MaximumLength(200);
        RuleFor(r => r.City).NotEmpty().MaximumLength(100);
        RuleFor(r => r.PostCode).MaximumLength(20);
    }
}

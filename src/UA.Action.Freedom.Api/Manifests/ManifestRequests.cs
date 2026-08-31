using FluentValidation;
using UA.Action.Freedom.Application.Manifests;

namespace UA.Action.Freedom.Api.Manifests;

/// <summary>
/// Body of <c>POST /manifests</c>. The reference is supplied by the caller — it is a document
/// number read out at a border, not a surrogate key.
/// </summary>
public sealed record CreateManifestRequest(
    string Id, string? Vin, int? ConvoyId, string? DeliveryNotes, bool FerryBookingComplete)
{
    public CreateManifestCommand ToCommand() => new(Id, Vin, ConvoyId, DeliveryNotes, FerryBookingComplete);
}

/// <summary>Body of <c>PUT /manifests/{id}</c>. The route supplies the reference.</summary>
public sealed record UpdateManifestRequest(
    string? Vin, int? ConvoyId, string? DeliveryNotes, bool FerryBookingComplete)
{
    public UpdateManifestCommand ToCommand(string id) => new(id, Vin, ConvoyId, DeliveryNotes, FerryBookingComplete);
}

/// <summary>Body of <c>PUT /manifests/{id}/teams/{leg}</c>.</summary>
public sealed record SetManifestTeamRequest(Guid PrimaryPersonId, Guid? SecondaryPersonId)
{
    public SetManifestTeamCommand ToCommand(string id, ManifestLeg leg) =>
        new(id, leg, PrimaryPersonId, SecondaryPersonId);
}

public sealed class CreateManifestRequestValidator : AbstractValidator<CreateManifestRequest>
{
    public CreateManifestRequestValidator()
    {
        RuleFor(r => r.Id).NotEmpty().MaximumLength(32);
        RuleFor(r => r.Vin).MaximumLength(32);
        RuleFor(r => r.DeliveryNotes).MaximumLength(2000);
    }
}

public sealed class UpdateManifestRequestValidator : AbstractValidator<UpdateManifestRequest>
{
    public UpdateManifestRequestValidator()
    {
        RuleFor(r => r.Vin).MaximumLength(32);
        RuleFor(r => r.DeliveryNotes).MaximumLength(2000);
    }
}

public sealed class SetManifestTeamRequestValidator : AbstractValidator<SetManifestTeamRequest>
{
    public SetManifestTeamRequestValidator() =>
        RuleFor(r => r.PrimaryPersonId).NotEmpty()
            .WithMessage("'Primary Person Id' must name the volunteer leading this leg.");
}

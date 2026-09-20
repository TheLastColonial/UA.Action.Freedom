using FluentValidation;
using UA.Action.Freedom.Application.Manifests;

namespace UA.Action.Freedom.Api.Manifests;

/// <summary>
/// Body of <c>PUT /manifests/{id}</c>. The route supplies the reference.
/// </summary>
/// <remarks>
/// The convoy and the vehicle are deliberately absent. They are the truck-list entry the manifest
/// is the paperwork for — its identity, not attributes of it — and the database holds them as a
/// composite foreign key. They used to be editable fields here, which is what let a manifest name
/// a truck on a different convoy, or none. A vehicle that leaves mid-journey is withdrawn from the
/// truck list (<c>DELETE /convoys/{id}/vehicles/{vin}</c>), which keeps this manifest intact.
/// </remarks>
public sealed record UpdateManifestRequest(string? DeliveryNotes, bool FerryBookingComplete)
{
    public UpdateManifestCommand ToCommand(string id) => new(id, DeliveryNotes, FerryBookingComplete);
}

public sealed class UpdateManifestRequestValidator : AbstractValidator<UpdateManifestRequest>
{
    public UpdateManifestRequestValidator() => RuleFor(r => r.DeliveryNotes).MaximumLength(2000);
}

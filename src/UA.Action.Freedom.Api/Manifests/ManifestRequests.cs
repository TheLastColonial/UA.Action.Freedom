using FluentValidation;
using UA.Action.Freedom.Application.Declarations;
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
public sealed record UpdateManifestRequest(string? DeliveryNotes)
{
    public UpdateManifestCommand ToCommand(string id) => new(id, DeliveryNotes);
}

public sealed class UpdateManifestRequestValidator : AbstractValidator<UpdateManifestRequest>
{
    public UpdateManifestRequestValidator() => RuleFor(r => r.DeliveryNotes).MaximumLength(2000);
}

/// <summary>
/// Body of <c>PUT /manifests/{id}/ens</c>: the ICS2 declaration this crossing was accepted under.
/// </summary>
/// <remarks>
/// Freedom does not submit the ENS, so this is a record of something that happened elsewhere — which
/// is why <paramref name="AcceptedAt"/> is supplied rather than stamped. It is ICS2's timestamp, and
/// the one a customs query will be about.
///
/// <para>
/// There is nothing here about the consignment. An ENS describes one in detail, but the description
/// never enters Freedom: it is composed for a filing sheet, read once, and what comes back is the
/// MRN. See <c>docs/adr/0003</c>.
/// </para>
/// </remarks>
public sealed record RecordEnsRequest(
    string Mrn, DateTimeOffset AcceptedAt, string FiledBy, string? FilingReference)
{
    public RecordEnsDeclarationCommand ToCommand(int convoyId, string vin) =>
        new(convoyId, vin, Mrn, AcceptedAt, FiledBy, FilingReference);
}

public sealed class RecordEnsRequestValidator : AbstractValidator<RecordEnsRequest>
{
    public RecordEnsRequestValidator()
    {
        // The shape is asserted here as well as in the handler, so a caller gets a 400 naming the
        // field rather than a bare outcome. EnsMrn.IsWellFormed is the single answer both use —
        // duplicating the pattern is how the two drift apart.
        RuleFor(request => request.Mrn)
            .NotEmpty()
            .Must(Domain.EnsMrn.IsWellFormed)
            .WithMessage(
                "An ICS2 MRN is eighteen characters: two digits of year, the ISO alpha-2 code of the "
                + "declaring country, then thirteen characters of reference and a check character, "
                + "all upper case. For example 25FR17551780961AT5.");

        RuleFor(request => request.FiledBy).NotEmpty().MaximumLength(200);
        RuleFor(request => request.FilingReference).MaximumLength(100);

        // A declaration accepted in the future is a typo, and it would be filed against a crossing
        // that cannot have happened. A day's slack covers a filer's clock and time zone.
        RuleFor(request => request.AcceptedAt)
            .LessThanOrEqualTo(_ => DateTimeOffset.UtcNow.AddDays(1))
            .WithMessage("An ICS2 declaration cannot have been accepted in the future.");
    }
}

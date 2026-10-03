using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Manifests;

/// <summary>
/// The facts an ICS2 declaration needs that no manifest knows.
/// </summary>
/// <remarks>
/// Supplied as a value rather than read from configuration in the handler, which is the boundary
/// <see cref="GmrSubmissionRequest"/> already draws: the Application composes what the manifest knows
/// and the API adapter supplies the environment. Every field is nullable because a misconfigured
/// deployment must produce a sheet that <em>says</em> what is missing, not a 500 — the point of the
/// sheet is to be read while there is still time to fix things.
/// </remarks>
/// <param name="DeclarantEori">
/// Ukrainian Action's EORI, as declarant and as carrier. Accompanied road freight has one filer, and
/// for these convoys the charity is it: its own volunteers drive.
/// </param>
/// <param name="ConsignorName">Who is sending the goods.</param>
/// <param name="OfficeOfFirstEntry">
/// The customs office where the goods first enter the EU. A property of the crossing, like
/// <c>Customs:RouteId</c> before it — see <c>docs/gotchas-and-open-questions.md</c> §9.
/// </param>
public sealed record EnsFilingEnvironment(
    string? DeclarantEori, string? ConsignorName, string? OfficeOfFirstEntry);

/// <summary>One goods item as it will be declared.</summary>
/// <param name="Number">
/// Its position in the consignment, 1-based. The goods item number is non-amendable in ICS2, so it is
/// derived from a stable order rather than left to whatever a caller renders.
/// </param>
/// <param name="Description">Plain-language description, from the packed item.</param>
/// <param name="CommodityCode">
/// At least six digits, or <see langword="null"/> when nobody has classified the item — in which case
/// the sheet reports it as missing rather than guessing.
/// </param>
public sealed record EnsGoodsItemReadModel(int Number, string Description, string? CommodityCode);

/// <summary>
/// One consignment: the goods on this manifest bound for one consignee.
/// </summary>
/// <remarks>
/// An ENS groups by consignment, not by package, and a consignment is what goes to one receiver. A
/// manifest's boxes may be for several, so they are grouped here rather than listed.
///
/// <para>
/// The consignee is named at organisation and region only — the same precision as the travelling
/// document. <see cref="ConsigneeAddressWithheld"/> is always true and says so out loud, because a
/// filer who saw no address might conclude there is none rather than fetching it from
/// <c>GET /receivers/{ref}/detail</c> under their own policy.
/// </para>
/// </remarks>
public sealed record EnsConsignmentReadModel(
    Guid ReceiverRef,
    string? ReceiverOrganisation,
    string? ReceiverRegion,
    bool ConsigneeAddressWithheld,
    int PackageCount,
    string PackageTypeCode,
    int GrossMassKg,
    IReadOnlyList<EnsGoodsItemReadModel> GoodsItems);

/// <summary>
/// Everything a Ground Officer needs to file this manifest's Entry Summary Declaration, and a list of
/// what is still missing.
/// </summary>
/// <remarks>
/// <strong>Deliberately incomplete.</strong> The consignee's delivery address is not here and never
/// will be: it lives in the <c>sensitive</c> schema behind the <c>receivers:detail</c> policy, this
/// sheet is composed on a connection that is <c>DENY</c>'d there, and a sheet listing precise
/// Ukrainian addresses would be a targeting document (<c>docs/domain/key-concepts.md</c> § Data
/// Sensitivity). The filer holds that address already; Freedom does not need to hand it to them.
///
/// <para>
/// <see cref="Missing"/> is the other half of the point. A declaration refused at the border costs a
/// convoy; a gap reported here costs a phone call.
/// </para>
/// </remarks>
public sealed record EnsFilingSheetReadModel(
    string ManifestId,
    string? DeclarantEori,
    string? CarrierEori,
    string? ConsignorName,
    string? OfficeOfFirstEntry,
    ChannelCrossing Crossing,
    int ModeOfTransportCode,
    string? ActiveMeansOfTransport,
    string? PassiveMeansOfTransport,
    IReadOnlyList<string> ItineraryCountryCodes,
    IReadOnlyList<EnsConsignmentReadModel> Consignments,
    int GrossMassKg,
    bool GrossMassProvisional,
    IReadOnlyList<string> Missing)
{
    /// <summary>Where the filer gets the consignee's address, which this sheet withholds.</summary>
    public string ConsigneeAddressSource => "GET /receivers/{ref}/detail";

    /// <summary>Whether everything ICS2 requires is present.</summary>
    public bool Complete => this.Missing.Count == 0;
}

/// <summary>One item of cargo on a manifest, flat, as an ENS needs to see it.</summary>
/// <remarks>
/// A row per item rather than per box, because an ENS declares goods items and Freedom packs boxes.
/// Reads <c>dbo.Receiver</c> only — organisation and region — so a query here that reached for a
/// delivery address would fail at the database rather than quietly succeed (§4.4).
/// </remarks>
public sealed record EnsGoodsLineReadModel(
    int BoxId,
    int WeightKg,
    bool Validated,
    Guid ReceiverRef,
    string? ReceiverOrganisation,
    string? ReceiverRegion,
    string ItemDescription,
    string? CommodityCode,
    string? CategoryName = null);

/// <summary>What would be filed for this manifest, or <c>null</c> if there is no such manifest.</summary>
public sealed record GetEnsFilingSheetQuery(string Id);

public sealed class GetEnsFilingSheetHandler(
    IManifestRepository repository,
    IConvoyRepository convoys,
    EnsFilingEnvironment environment)
    : IQueryHandler<GetEnsFilingSheetQuery, EnsFilingSheetReadModel?>
{
    public async Task<EnsFilingSheetReadModel?> HandleAsync(
        GetEnsFilingSheetQuery query, CancellationToken cancellationToken)
    {
        var manifest = await repository.GetByIdAsync(query.Id, cancellationToken);

        if (manifest is null)
        {
            return null;
        }

        var convoy = await convoys.GetByIdAsync(manifest.ConvoyId, cancellationToken);
        var crossing = convoy?.CrossingMode ?? ChannelCrossing.Ferry;
        var plate = await repository.GetVehiclePlateAsync(query.Id, cancellationToken);
        var vehicleKg = await repository.GetVehicleWeightKgAsync(query.Id, cancellationToken);
        var lines = await repository.GetEnsGoodsLinesAsync(query.Id, cancellationToken);
        var route = convoy is null
            ? []
            : await convoys.GetRouteAsync(manifest.ConvoyId, cancellationToken);

        var missing = new List<string>();

        // On a ferry the vessel is the active means of transport and the lorry is only carried; on the
        // shuttle the lorry is active in its own right. The plate is the passive means either way.
        var vessel = EnsTransportMode.NeedsAVessel(crossing) ? convoy?.VesselImo : null;
        var active = EnsTransportMode.NeedsAVessel(crossing) ? vessel : plate;

        if (EnsTransportMode.NeedsAVessel(crossing) && string.IsNullOrWhiteSpace(vessel))
        {
            missing.Add(
                "The vessel IMO number for this ferry crossing. It is the active means of transport, it "
                + "must come from the official list for the route, and it can never be amended once filed.");
        }

        if (string.IsNullOrWhiteSpace(plate))
        {
            missing.Add("The vehicle's registration plate, which is the passive means of transport.");
        }

        if (string.IsNullOrWhiteSpace(environment.DeclarantEori))
        {
            missing.Add("The declarant's EORI number (Customs:HaulierEori is not configured).");
        }

        if (string.IsNullOrWhiteSpace(environment.ConsignorName))
        {
            missing.Add("The consignor's name (Ens:ConsignorName is not configured).");
        }

        if (string.IsNullOrWhiteSpace(environment.OfficeOfFirstEntry))
        {
            missing.Add(
                "The customs office of first entry (Ens:OfficeOfFirstEntry is not configured).");
        }

        foreach (var stop in route.Where(stop => string.IsNullOrWhiteSpace(stop.CountryCode)))
        {
            missing.Add(
                $"An ISO country code for route stop {stop.Sequence} ({stop.Country ?? "unnamed"}). "
                + "EU customs cannot complete its pre-arrival risk assessment without every country "
                + "the goods pass through.");
        }

        var consignments = Group(lines, missing);

        if (consignments.Count == 0)
        {
            missing.Add("Any cargo at all: this manifest has no boxes, so there is nothing to declare.");
        }

        var cargoKg = lines.DistinctBy(line => line.BoxId).Sum(line => line.WeightKg);
        var unvalidated = lines.DistinctBy(line => line.BoxId).Count(line => !line.Validated);

        if (unvalidated > 0)
        {
            // An unvalidated box weighs zero until a Loader says otherwise, so the total understates
            // the load. Filing that silently is how a weight discrepancy becomes a border inspection.
            missing.Add(
                $"{unvalidated} box(es) have not been validated, so the gross mass is provisional and "
                + "understates the load. Have a Loader validate them before filing.");
        }

        return new EnsFilingSheetReadModel(
            manifest.Id,
            environment.DeclarantEori,
            // Declarant and carrier are the same party: accompanied road freight has one filer, and
            // for these convoys the charity's own volunteers drive.
            environment.DeclarantEori,
            environment.ConsignorName,
            environment.OfficeOfFirstEntry,
            crossing,
            EnsTransportMode.For(crossing),
            active,
            plate,
            route
                .OrderBy(stop => stop.Sequence)
                .Select(stop => stop.CountryCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!.ToUpperInvariant())
                .Distinct()
                .ToList(),
            consignments,
            ManifestWeight.Total(vehicleKg, cargoKg),
            unvalidated > 0,
            missing);
    }

    /// <summary>
    /// Groups the cargo into one consignment per consignee, in the order the receivers first appear,
    /// and numbers each consignment's goods items from one.
    /// </summary>
    private static List<EnsConsignmentReadModel> Group(
        IReadOnlyList<EnsGoodsLineReadModel> lines, List<string> missing)
    {
        var consignments = new List<EnsConsignmentReadModel>();

        foreach (var group in lines.GroupBy(line => line.ReceiverRef))
        {
            var items = group
                .Select((line, index) => new EnsGoodsItemReadModel(
                    index + 1, line.ItemDescription, line.CommodityCode))
                .ToList();

            foreach (var line in group.Where(line => string.IsNullOrWhiteSpace(line.CommodityCode)))
            {
                var category = line.CategoryName is { } name ? $" nor its category \"{name}\"" : string.Empty;

                missing.Add(
                    $"A commodity code for \"{line.ItemDescription}\": neither the item{category} has an EU code. "
                    + $"ICS2 requires at least six digits per goods item; {EnsCommodity.HumanitarianAid} covers humanitarian aid.");
            }

            // A box is the package, so the package count is how many distinct boxes this consignee's
            // items came out of — not how many items there are.
            var boxes = group.DistinctBy(line => line.BoxId).ToList();
            var first = group.First();

            consignments.Add(new EnsConsignmentReadModel(
                group.Key,
                first.ReceiverOrganisation,
                first.ReceiverRegion,
                ConsigneeAddressWithheld: true,
                boxes.Count,
                EnsPackaging.Box,
                boxes.Sum(box => box.WeightKg),
                items));
        }

        return consignments;
    }
}

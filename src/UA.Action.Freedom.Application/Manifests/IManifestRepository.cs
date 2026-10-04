using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Manifests;

/// <summary>
/// Persistence port for <see cref="ManifestReadModel"/> and its cargo.
/// </summary>
/// <remarks>
/// Driver teams are gone from here. A manifest no longer keeps its own crew: who is driving is a
/// fact about the vehicle on the convoy, recorded once on the crew row and read through
/// <see cref="IConvoyVehicleRepository"/>.
/// </remarks>
public interface IManifestRepository
{
    Task<ManifestReadModel?> GetByIdAsync(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ManifestReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken);

    /// <summary>The manifest already opened for this truck-list entry, if there is one.</summary>
    /// <remarks>
    /// One manifest per vehicle per convoy: arrival asks each vehicle for its finished manifest and
    /// has to get one answer. The unique constraint is the real guard; this makes the refusal a
    /// 409 that names the existing reference rather than a foreign-key exception.
    /// </remarks>
    Task<ManifestReadModel?> GetForVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken);

    Task AddAsync(ManifestReadModel manifest, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the notes and the ferry booking. Cannot touch the convoy, the vehicle, the status
    /// or the GMR stamp — the first two are the manifest's identity, the last two belong to the
    /// transitions.
    /// </summary>
    Task<bool> UpdateAsync(ManifestReadModel manifest, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Moves the manifest from <paramref name="from"/> to <paramref name="to"/>, but only if it
    /// is still in <paramref name="from"/>. Returns false when it has moved underneath us, which
    /// is how two people pressing the same button resolve to one transition.
    /// </summary>
    Task<bool> TransitionAsync(string id, ManifestStatus from, ManifestStatus to, CancellationToken cancellationToken);

    /// <summary>
    /// Confirms the manifest and freezes it in one statement, returning the stamp it wrote.
    /// </summary>
    /// <remarks>
    /// The freeze and the status change are the same write because a confirmed manifest whose
    /// GMR is on its way must never be editable, not even for the width of a second statement.
    /// Returns null when the manifest was not in <paramref name="from"/> any more.
    /// </remarks>
    Task<DateTime?> ConfirmAndFreezeAsync(string id, ManifestStatus from, CancellationToken cancellationToken);

    /// <summary>The kerb weight of the manifest's vehicle.</summary>
    Task<int> GetVehicleWeightKgAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// The registration plate of the manifest's vehicle — what a border officer reads, and what
    /// HMRC is told.
    /// </summary>
    /// <remarks>
    /// Not the VIN. <c>GmrSubmissionRequest.VehicleRegistration</c> and the printed document both
    /// say "the plate the border expects to see", and both were being handed
    /// <c>Manifest.Vin</c> — the chassis number, which is not on the front of the vehicle.
    /// </remarks>
    Task<string?> GetVehiclePlateAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// The manifest's vehicle's cargo capacity, all-null when its capacity has never been measured.
    /// </summary>
    Task<VehicleCargoCapacityReadModel> GetVehicleCargoCapacityAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// One line per box for the document that travels with the vehicle: what is being carried,
    /// and roughly where to.
    /// </summary>
    /// <remarks>
    /// Reads <c>dbo.Receiver</c> only — organisation and region. The delivery address lives in
    /// the <c>sensitive</c> schema, which this connection is <c>DENY</c>'d on, so a query here
    /// that reached for one would fail at the database rather than quietly succeed (§4.4).
    /// </remarks>
    Task<IReadOnlyList<ManifestDocumentLineReadModel>> GetDocumentLinesAsync(
        string id, CancellationToken cancellationToken);

    /// <summary>
    /// One row per packed item, for the ICS2 filing sheet: what it is, what it is classified as, which
    /// box it came out of and which receiver it is for.
    /// </summary>
    /// <remarks>
    /// Per item rather than per box, because an ENS declares goods items while Freedom packs boxes.
    /// Reads <c>dbo.Receiver</c> only — reference, organisation and region. The delivery address lives
    /// in the <c>sensitive</c> schema, which this connection is <c>DENY</c>'d on, so a query here that
    /// reached for one would fail at the database rather than quietly succeed (§4.4).
    /// </remarks>
    Task<IReadOnlyList<EnsGoodsLineReadModel>> GetEnsGoodsLinesAsync(
        string id, CancellationToken cancellationToken);
}

/// <summary>
/// The durable hand-off from the API to the Customs Worker.
/// </summary>
/// <remarks>
/// A port so that approving a manifest can be tested without a storage account. The message
/// carries a manifest reference and vehicle registration and <strong>no receiver name, contact
/// or address</strong> — the worker has no business knowing where in Ukraine a load is going,
/// and its logs are retained (recommendations §4.1, §4.4).
/// </remarks>
public interface IManifestWorkQueue
{
    Task EnqueueGmrSubmissionAsync(GmrSubmissionRequest submission, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the Manifest Worker to render the document that travels with the vehicle.
    /// </summary>
    /// <remarks>
    /// The whole document is composed here and put on the queue, rather than the worker being
    /// given a reference to look up. That is what lets the worker have no database access at
    /// all: it cannot read a delivery address because it cannot read anything, and the request
    /// type has nowhere to carry one.
    /// </remarks>
    Task EnqueueDocumentAsync(ManifestDocumentRequest document, CancellationToken cancellationToken);

    /// <summary>
    /// Asks for a French customs logistics envelope (ELO) for the vehicle this manifest covers.
    /// </summary>
    /// <remarks>
    /// The other prong of the fork in <c>docs/process.puml</c>. France requires an envelope per
    /// transport unit at the Smart Border, and it is what pairs the lorry's customs formalities with
    /// its physical crossing — no envelope, no sailing.
    /// </remarks>
    Task EnqueueEloEnvelopeAsync(EloEnvelopeRequest envelope, CancellationToken cancellationToken);
}

/// <summary>One box on the document that travels with the vehicle.</summary>
public sealed record ManifestDocumentLineReadModel(
    int BoxId, int WeightKg, int ItemCount, string? ReceiverOrganisation, string? ReceiverRegion);

/// <summary>
/// Everything the printed manifest is allowed to contain.
/// </summary>
/// <remarks>
/// Deliberately has nowhere to put a street address, a contact name or a phone number. The
/// document crosses several borders where it may be inspected or seized, and one listing precise
/// Ukrainian delivery addresses is a targeting document (docs/domain/key-concepts.md § Data
/// Sensitivity). Region is as precise as it gets. The wire shape must stay in step with
/// <c>UA.Action.Freedom.ManifestWorker.Documents.ManifestDocumentRequest</c>; the two projects
/// do not share a type because the worker is a separate deployable.
/// </remarks>
public sealed record ManifestDocumentRequest(
    string ManifestId,
    string? VehicleRegistration,
    int VehicleWeightKg,
    int CargoKg,
    int CrewAndBagsKg,
    int FuelKg,
    int TotalKg,
    IReadOnlyList<ManifestDocumentLineReadModel> Lines);

/// <param name="ManifestId">Which manifest this movement is for.</param>
/// <param name="VehicleRegistration">The plate the border expects to see.</param>
/// <param name="DepartsAt">
/// Planned departure, taken from the convoy. HMRC needs a crossing time, and the convoy is what
/// knows it — the manifest only knows which convoy it is on.
/// </param>
/// <remarks>
/// The ICS2 ENS MRN is deliberately <strong>not</strong> here, and that is a finding rather than an
/// omission. GVMS does have a field for it — <c>sAndSMasterRefNum</c>, "the Movement Reference Number
/// for a Safety &amp; Security declaration … applies to both ENS and EXS" — but it hangs off a
/// declaration container, and every container requires a primary identifier Freedom does not hold:
/// <c>customsDeclarations[].customsDeclarationId</c> is a CDS DUCR for an outbound movement,
/// <c>tirDeclarations[].tirCarnetId</c> a TIR carnet number, <c>ataDeclarations[].ataCarnetId</c> an
/// ATA one. Putting the ENS MRN in <c>customsDeclarationId</c> would file an ICS2 reference as a CDS
/// one. The spec also scopes ICS2 MRNs to the <c>GB_TO_NI</c> direction, and these movements are
/// <c>UK_OUTBOUND</c>. See <c>docs/gotchas-and-open-questions.md</c> §5b.
/// </remarks>
public sealed record GmrSubmissionRequest(
    string ManifestId, string VehicleRegistration, DateTime? DepartsAt);

/// <param name="ManifestId">Which manifest's vehicle is crossing.</param>
/// <param name="Profile">
/// What the envelope declares about the crossing, which is what French customs uses to decide which
/// formalities the envelope must contain.
/// </param>
/// <param name="DeclarationIdentifiers">
/// The formalities this crossing is being paired to. Under
/// <see cref="EloCrossingProfile.HumanitarianAidToUkraine"/> that is exactly one ICS2 ENS MRN, and
/// <c>ApproveManifestHandler</c> refuses to approve a manifest that has none — a loaded lorry naming
/// no formality is refused by French customs under ENV_CTR_RG08, and by then the manifest is frozen.
/// </param>
/// <remarks>
/// An ELO carries no goods description, no weights, no consignor or consignee, not even a
/// registration — those belong to the customs declarations the envelope references. So unlike
/// <see cref="ManifestDocumentRequest"/>, which had to be kept narrow on purpose, this one has
/// nothing sensitive to withhold: the API itself has nowhere to put it. An MRN is a reference to a
/// declaration another authority holds, not a description of one.
///
/// <para>
/// The identifiers are per-manifest data, which is what this record's own remarks predicted would
/// happen "when Freedom obtains a real ENS from ICS2". They used to be supplied by the adapter from
/// <c>Elo:PlaceholderDeclarationIdentifier</c>, a stand-in the local stub accepted and real French
/// customs refuses with FONC-ERR-004. That setting is gone.
/// </para>
/// </remarks>
public sealed record EloEnvelopeRequest(
    string ManifestId, EloCrossingProfile Profile, IReadOnlyList<string> DeclarationIdentifiers);

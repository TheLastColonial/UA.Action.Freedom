using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Manifests;

/// <summary>
/// The French logistics envelope issued for this manifest's vehicle.
/// </summary>
/// <remarks>
/// References and a status, and nothing about the load — an ELO has no field for cargo, and this read
/// model has none either. <see cref="NumeroDossier"/> is what a later modify or retrieve is keyed on;
/// <see cref="Jeton"/> is what the barcode encodes.
/// </remarks>
/// <param name="ManifestId">Which manifest's vehicle this envelope covers.</param>
/// <param name="Jeton">The envelope token French customs minted.</param>
/// <param name="NumeroDossier">The envelope file number.</param>
/// <param name="Statut">
/// FERMEE on creation, then APPAIREE, EMBARQUEE, DEBARQUEE as the lorry is paired at the port,
/// boarded and landed. Freedom does not yet follow it past FERMEE — see
/// <c>docs/gotchas-and-open-questions.md</c> §8.
/// </param>
/// <param name="DeclarationCount">How many customs formalities the envelope names.</param>
/// <param name="SubmittedAt">When Freedom created it.</param>
/// <param name="HasBarcodeDocument">
/// Whether a barcode document can be fetched from <c>GET /manifests/{id}/elo/document</c>.
/// </param>
public sealed record EloEnvelopeReadModel(
    string ManifestId,
    string? Jeton,
    string? NumeroDossier,
    string? Statut,
    int DeclarationCount,
    DateTimeOffset SubmittedAt,
    bool HasBarcodeDocument);

/// <summary>
/// Reads back the envelopes the Customs Worker has obtained.
/// </summary>
/// <remarks>
/// The read half of a durable hand-off the API only ever writes to. The worker has no database — that
/// is deliberate, so it cannot read a delivery address even in principle — so what it learns from
/// French customs comes back through the document store rather than through a table. This port is
/// what lets a Dispatcher see that an envelope exists without the worker being given a connection
/// string.
/// </remarks>
public interface IEloEnvelopeStore
{
    Task<EloEnvelopeReadModel?> GetAsync(string manifestId, CancellationToken cancellationToken);

    /// <summary>The barcode document, or <see langword="null"/> if there is none to fetch.</summary>
    Task<byte[]?> GetBarcodeAsync(string manifestId, CancellationToken cancellationToken);
}

/// <summary>Has this manifest's vehicle got its French logistics envelope yet?</summary>
public sealed record GetManifestEloQuery(string Id);

public sealed class GetManifestEloHandler(IEloEnvelopeStore envelopes)
    : IQueryHandler<GetManifestEloQuery, EloEnvelopeReadModel?>
{
    // Deliberately does not check that the manifest exists first. An envelope only exists because a
    // manifest was approved, so the extra round trip could not change the answer — and both "no such
    // manifest" and "not submitted yet" are a 404 to the caller.
    public Task<EloEnvelopeReadModel?> HandleAsync(
        GetManifestEloQuery query, CancellationToken cancellationToken) =>
        envelopes.GetAsync(query.Id, cancellationToken);
}

/// <summary>The barcode a driver presents at the French Smart Border.</summary>
public sealed record GetManifestEloDocumentQuery(string Id);

public sealed class GetManifestEloDocumentHandler(IEloEnvelopeStore envelopes)
    : IQueryHandler<GetManifestEloDocumentQuery, byte[]?>
{
    public Task<byte[]?> HandleAsync(
        GetManifestEloDocumentQuery query, CancellationToken cancellationToken) =>
        envelopes.GetBarcodeAsync(query.Id, cancellationToken);
}

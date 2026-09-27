namespace UA.Action.Freedom.CustomsWorker.Elo;

/// <summary>
/// The envelope French customs issued, as Freedom keeps it.
/// </summary>
/// <remarks>
/// This is what the API serves back on <c>GET /manifests/{id}/elo</c>, so it is the wire contract of
/// that endpoint as much as a storage record. It holds references and a status and nothing else:
/// there is no reason for a document store to know anything about the load.
/// </remarks>
/// <param name="ManifestId">Which manifest's vehicle this envelope covers.</param>
/// <param name="Jeton">The envelope token French customs minted.</param>
/// <param name="NumeroDossier">The envelope file number, used to modify or retrieve it later.</param>
/// <param name="Statut">
/// Where the envelope has reached: FERMEE on creation, then APPAIREE, EMBARQUEE, DEBARQUEE as the
/// lorry is paired at the port, boarded and landed.
/// </param>
/// <param name="DeclarationCount">How many formalities the envelope names.</param>
/// <param name="SubmittedAt">When Freedom created it.</param>
/// <param name="HasBarcodeDocument">
/// Whether a barcode document was stored beside this record. False means the envelope exists at
/// French customs but its PDF did not arrive in a form that could be saved.
/// </param>
public sealed record EloEnvelopeDocument(
    string ManifestId,
    string? Jeton,
    string? NumeroDossier,
    string? Statut,
    int DeclarationCount,
    DateTimeOffset SubmittedAt,
    bool HasBarcodeDocument);

/// <summary>
/// Where issued envelopes and their barcode documents are kept.
/// </summary>
/// <remarks>
/// A port, like <see cref="IGmrDocumentStore"/>, so the processor's rules about what is stored and
/// when a message may be deleted are testable without a storage account.
/// </remarks>
public interface IEloDocumentStore
{
    /// <summary>
    /// Stores the envelope, and its barcode alongside when one arrived.
    /// </summary>
    /// <param name="envelope">The reference record, saved as JSON.</param>
    /// <param name="barcodePdf">
    /// The decoded barcode document, or <see langword="null"/> when French customs sent none or sent
    /// something that is not base64. The envelope is stored either way — it exists at customs, and
    /// forgetting its number would be worse than lacking its PDF.
    /// </param>
    Task SaveAsync(EloEnvelopeDocument envelope, byte[]? barcodePdf, CancellationToken cancellationToken);
}

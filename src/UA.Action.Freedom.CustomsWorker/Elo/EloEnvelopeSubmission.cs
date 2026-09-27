namespace UA.Action.Freedom.CustomsWorker.Elo;

/// <summary>
/// An envelope request as it arrives on the queue.
/// </summary>
/// <remarks>
/// The wire shape must stay in step with what
/// <c>UA.Action.Freedom.Api.Messaging.AzureManifestWorkQueue.EnqueueEloEnvelopeAsync</c> writes. The
/// two projects deliberately do not share a type: the worker is a separate deployable and in the
/// target design an Azure Function, so this is a contract between processes rather than a class
/// reference. A component test on the producer side pins the literal JSON.
///
/// <para>
/// Note what is not here. No receiver, no address, no contact, no box, no weight — not because they
/// are filtered out, but because an ELO has no field for any of them. The envelope references
/// customs declarations and describes a crossing; what is in the lorry is the declarations'
/// business. That makes the redaction structural, in the same way
/// <c>ManifestDocumentRequest</c>'s is.
/// </para>
///
/// <para>
/// Every property is nullable or defaulted, because a message is untrusted input: a producer at a
/// different version, a hand-written test message or a replayed body must deserialise into
/// something the processor can reject cleanly rather than throw on.
/// </para>
/// </remarks>
/// <param name="ManifestId">Which manifest's vehicle is crossing. Also the document's blob name.</param>
/// <param name="CrossingDirection"><c>Import</c> for UK to France, which is every convoy today.</param>
/// <param name="LorryType"><c>Loaded</c> or <c>Empty</c>.</param>
/// <param name="TirAta">Whether TIR or ATA formalities apply.</param>
/// <param name="HasTransportContract">Whether a transport contract exists.</param>
/// <param name="Postal">Whether the unit carries postal goods.</param>
/// <param name="EmptyPackaging">Whether the unit carries empty packaging or pallets.</param>
/// <param name="SanitaryOrPhytosanitary">Whether the unit carries goods under sanitary control.</param>
/// <param name="FisheryProducts">Whether the unit carries fishery products.</param>
/// <param name="DeclarationIdentifiers">
/// The customs formalities the envelope pairs to the crossing — an ENS, an import MRN, a transit
/// MRN. French customs validates each one and answers per identifier.
/// </param>
/// <param name="Traceparent">
/// W3C trace context of the approval that queued this, so the consumer span can link back to it.
/// </param>
public sealed record EloEnvelopeSubmission(
    string? ManifestId,
    string? CrossingDirection,
    string? LorryType,
    bool TirAta = false,
    bool HasTransportContract = false,
    bool Postal = false,
    bool EmptyPackaging = false,
    bool SanitaryOrPhytosanitary = false,
    bool FisheryProducts = false,
    IReadOnlyList<string>? DeclarationIdentifiers = null,
    string? Traceparent = null);

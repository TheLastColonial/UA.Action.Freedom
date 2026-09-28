using System.Diagnostics;
using System.Text.Json;
using EDI.ELO;
using Microsoft.Extensions.Logging;
using UA.Action.Freedom.CustomsWorker.Queueing;
using UA.Action.Freedom.CustomsWorker.Telemetry;
using UA.Action.Freedom.Telemetry;

namespace UA.Action.Freedom.CustomsWorker.Elo;

/// <summary>
/// Takes one envelope request off the queue and creates the logistics envelope with French customs.
/// </summary>
/// <remarks>
/// The queue-triggered sibling of <see cref="Customs.GmrSubmissionProcessor"/>, and the same shape:
/// the decision this class exists to make is not how to call the ELO API but when it is safe to
/// delete the message.
///
/// <para>
/// With one rule the GMR side does not need. <strong>Nothing is dead-lettered once French customs
/// has accepted the envelope.</strong> Creation is not idempotent and the envelope exists from that
/// moment, identified by a <c>numeroDossier</c> only this response carries; discarding the message
/// would leave an envelope at customs that nobody in Freedom can name, modify or present at a
/// border. So a barcode that cannot be decoded is stored as an envelope without one, and the message
/// is completed — the barcode can be fetched again, a forgotten file number cannot.
/// </para>
/// </remarks>
public sealed class EloEnvelopeProcessor(
    IEloWorkQueue queue,
    IEloClient elo,
    IEloTokenProvider tokens,
    IEloDocumentStore documents,
    ILogger<EloEnvelopeProcessor> logger,
    QueueFlowMetrics? queueMetrics = null,
    CustomsMetrics? customsMetrics = null,
    TimeProvider? time = null)
{
    /// <summary>The message code French customs expects on a creation request.</summary>
    private const string CreateMessageCode = "ENV_CRE01";

    private readonly QueueFlowMetrics _queueMetrics = queueMetrics ?? QueueFlowMetrics.Unobserved;
    private readonly CustomsMetrics _customs = customsMetrics ?? CustomsMetrics.Unobserved;
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>
    /// camelCase, and case-insensitive on the way in — the same configuration the Freedom
    /// Application serialises with. <see cref="JsonSerializerOptions.Default"/> would match
    /// case-sensitively and hand back an object with every property null, which surfaces as "carries
    /// no manifest reference" for every message with nothing to suggest capitalisation is the cause.
    /// </summary>
    private static readonly JsonSerializerOptions QueueMessageFormat = JsonSerializerOptions.Web;

    /// <summary>
    /// Processes at most one message.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if a message was taken off the queue, whatever became of it;
    /// <see langword="false"/> if the queue was empty.
    /// </returns>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var item = await queue.ReceiveAsync(cancellationToken);

        if (item is null)
        {
            return false;
        }

        _queueMetrics.Received(QueueNames.EloEnvelopes, item.InsertedOn, item.DequeueCount);

        EloEnvelopeSubmission? request;

        try
        {
            request = JsonSerializer.Deserialize<EloEnvelopeSubmission>(item.Body, QueueMessageFormat);
        }
        catch (JsonException exception)
        {
            // Note what could not be read, never what it said.
            logger.LogError(
                exception, "Work item {MessageId} is not a readable envelope request.", item.MessageId);
            await DeadLetter(item, "unreadable", "Message body could not be deserialised.", null, cancellationToken);
            return true;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.ManifestId))
        {
            logger.LogError("Work item {MessageId} carries no manifest reference.", item.MessageId);
            await DeadLetter(
                item, "no_manifest_ref", "Message body carries no manifest reference.", null, cancellationToken);
            return true;
        }

        using var activity = QueueTelemetry.StartConsumer(
            QueueNames.EloEnvelopes, item.MessageId, request.Traceparent);
        using var scope = logger.BeginScope(
            "Manifest {ManifestId}, work item {MessageId}", request.ManifestId, item.MessageId);

        var declarations = request.DeclarationIdentifiers ?? [];

        // ENV_CTR_RG08: a loaded transport unit must name at least one formality, and French customs
        // answers an envelope naming none with FONC-ERR-004. The answer is already known, so this
        // does not need a round trip — and the outcome would be a dead-letter either way.
        if (LorryTypeOf(request) == TypeCamion.PLEIN && declarations.Count == 0)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            logger.LogError(
                "The envelope request for manifest {ManifestId} names no declaration, which French customs "
                + "refuses for a loaded lorry (ENV_CTR_RG08).",
                request.ManifestId);
            await DeadLetter(
                item,
                "no_declaration",
                "A loaded lorry's envelope must name at least one declaration (ENV_CTR_RG08).",
                null,
                cancellationToken);
            return true;
        }

        var started = Stopwatch.GetTimestamp();
        var created = false;

        try
        {
            var correlationId = Guid.NewGuid().ToString();

            var response = await elo.CreerENVAsync(
                authorization: $"Bearer {await tokens.GetAsync(cancellationToken)}",
                messageCode: CreateMessageCode,
                // On a creation the functional identifier may be the correlation identifier; it is
                // only for modify and retrieve that it must carry the envelope number, which does
                // not exist yet.
                functionalId: correlationId,
                messageId: Guid.NewGuid().ToString(),
                correlationId: correlationId,
                body: ToRequest(request, declarations),
                cancellationToken: cancellationToken);

            // From here the envelope exists at French customs. Nothing below may dead-letter.
            created = true;
            _customs.EloSubmissionCompleted("accepted", Stopwatch.GetElapsedTime(started));

            var barcode = Barcode.From(response, logger, request.ManifestId);

            await documents.SaveAsync(
                new EloEnvelopeDocument(
                    request.ManifestId,
                    response.Enveloppe?.Jeton,
                    response.Enveloppe?.NumeroDossier,
                    response.Enveloppe?.Statut?.ToString(),
                    response.Enveloppe?.NombreDeclaration ?? declarations.Count,
                    _time.GetUtcNow(),
                    barcode is not null),
                barcode,
                cancellationToken);

            logger.LogInformation(
                "Created a French logistics envelope for manifest {ManifestId}.", request.ManifestId);

            await queue.CompleteAsync(item, cancellationToken);
            _queueMetrics.Settled(QueueNames.EloEnvelopes, QueueOutcome.Completed);
        }
        catch (EloApiException exception) when (!created && exception.StatusCode is >= 400 and < 500)
        {
            // French customs has judged the envelope — a declaration in the wrong format, one
            // already paired to an embarked lorry, a formality that does not match the crossing.
            // Retrying produces the same answer, so this needs a person.
            _customs.EloSubmissionCompleted("rejected", Stopwatch.GetElapsedTime(started));
            activity?.SetStatus(ActivityStatusCode.Error);

            var code = RefusalCode(exception);

            // The status and the error code, never the exception: its message carries French
            // customs' response body, and an error label quotes the declaration it objected to.
            logger.LogError(
                "French customs refused the envelope for manifest {ManifestId} with {StatusCode} {ErrorCode}.",
                request.ManifestId,
                exception.StatusCode,
                code);

            await DeadLetter(
                item,
                "elo_rejected",
                $"French customs refused the envelope with {exception.StatusCode} {code}.",
                exception.StatusCode,
                cancellationToken);
        }
        catch (Exception exception)
        {
            // Transient: a timeout, a 5xx, a dropped connection — or a store that failed after the
            // envelope was created. Leave the message alone and let its visibility timeout expire.
            // A retry after a successful create makes a second envelope at customs, which is
            // unwanted but visible and recoverable; completing the message here loses the first one
            // for good.
            if (!created)
            {
                _customs.EloSubmissionCompleted("error", Stopwatch.GetElapsedTime(started));
            }

            _queueMetrics.Settled(QueueNames.EloEnvelopes, QueueOutcome.LeftForRetry);
            activity?.SetStatus(ActivityStatusCode.Error);

            LogCouldNotFinish(exception, request.ManifestId, item.MessageId, created);
        }

        return true;
    }

    private async Task DeadLetter(
        EloWorkItem item, string reason, string message, int? httpStatus, CancellationToken cancellationToken)
    {
        await queue.DeadLetterAsync(item, message, cancellationToken);

        _customs.DeadLettered(reason, httpStatus);
        _queueMetrics.Settled(QueueNames.EloEnvelopes, QueueOutcome.DeadLettered);
    }

    private void LogCouldNotFinish(Exception exception, string manifestId, string messageId, bool created)
    {
        if (created)
        {
            // Worth its own line: the envelope is real, and the retry will create another.
            logger.LogWarning(
                exception,
                "French customs created an envelope for manifest {ManifestId} but it could not be stored; "
                + "leaving work item {MessageId} to be retried, which will create a second envelope.",
                manifestId,
                messageId);
            return;
        }

        const string Message =
            "Could not reach French customs for manifest {ManifestId}; leaving work item {MessageId} "
            + "to be retried.";

        if (exception is EloApiException api)
        {
            // A 5xx still carries the response body in its message; log the status only.
            logger.LogWarning(Message + " Customs answered {StatusCode}.", manifestId, messageId, api.StatusCode);
            return;
        }

        logger.LogWarning(exception, Message, manifestId, messageId);
    }

    /// <summary>
    /// The functional error code French customs assigned, when the refusal was a modelled one.
    /// </summary>
    /// <remarks>
    /// A bounded value like <c>FONC-ERR-004</c>, which is the part of a refusal worth keeping: it
    /// separates a malformed declaration from one already paired to another lorry. The accompanying
    /// <c>libelleErreur</c> is free text quoting the declaration, and is deliberately not logged.
    /// </remarks>
    private static string RefusalCode(EloApiException exception) => exception switch
    {
        EloApiException<ENV_CRE03> refusal => refusal.Result.InformationsErreur?.Statut ?? "unknown",
        _ => "unknown",
    };

    private static TypeCamion LorryTypeOf(EloEnvelopeSubmission request) =>
        string.Equals(request.LorryType, nameof(TypeCamion.VIDE), StringComparison.OrdinalIgnoreCase)
        || string.Equals(request.LorryType, "Empty", StringComparison.OrdinalIgnoreCase)
            ? TypeCamion.VIDE
            : TypeCamion.PLEIN;

    private static SensTraversee DirectionOf(EloEnvelopeSubmission request) =>
        string.Equals(request.CrossingDirection, nameof(SensTraversee.EXPORT), StringComparison.OrdinalIgnoreCase)
            ? SensTraversee.EXPORT
            : SensTraversee.IMPORT;

    private static ENV_CRE01 ToRequest(
        EloEnvelopeSubmission request, IReadOnlyList<string> declarations) => new()
    {
        InformationsAppairage = new InformationsAppairage
        {
            SensTraversee = DirectionOf(request),
            TypeCamion = LorryTypeOf(request),
            EstTIRATA = request.TirAta,
            PossedeContratTransport = request.HasTransportContract,
            EstPostal = request.Postal,
            EstEmballageVide = request.EmptyPackaging,
            EstSPS = request.SanitaryOrPhytosanitary,
            EstProduitPeche = request.FisheryProducts,
        },
        IdentifiantsDeclaration = [.. declarations],
    };

    /// <summary>
    /// Pulls the barcode out of a creation response, from whichever of the two places it arrived in.
    /// </summary>
    private static class Barcode
    {
        internal static byte[]? From(ENV_CRE02 response, ILogger logger, string manifestId)
        {
            // The spec's schema puts pdf inside enveloppe; its worked examples put it beside it.
            // Read whichever is there rather than picking a side.
            var base64 = response.Enveloppe?.Pdf ?? response.Pdf;

            if (string.IsNullOrWhiteSpace(base64))
            {
                logger.LogWarning(
                    "French customs returned no barcode document for manifest {ManifestId}; the envelope "
                    + "is stored without one.",
                    manifestId);
                return null;
            }

            try
            {
                return Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                // Decoded here rather than by the deserialiser on purpose: a value that is not
                // base64 must be something this processor can carry on past, not an exception that
                // loses the envelope reference alongside the document.
                logger.LogWarning(
                    "The barcode document for manifest {ManifestId} was not base64; the envelope is "
                    + "stored without one.",
                    manifestId);
                return null;
            }
        }
    }
}

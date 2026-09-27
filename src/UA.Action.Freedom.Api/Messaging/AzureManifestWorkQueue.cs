using System.Diagnostics;
using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Telemetry;

namespace UA.Action.Freedom.Api.Messaging;

/// <summary>
/// Puts an approved manifest's border paperwork on its queues: the Goods Movement Reference and the
/// French logistics envelope for the Customs Worker, the travelling document for the Manifest
/// Worker.
/// </summary>
/// <remarks>
/// Pull, not push: the workers drain these queues and poll for outcomes, and Freedom exposes
/// no inbound callback endpoint (docs/recommendations.md §4.1).
///
/// The wire shapes must match <c>UA.Action.Freedom.CustomsWorker.Customs.GmrSubmission</c>,
/// <c>UA.Action.Freedom.CustomsWorker.Elo.EloEnvelopeSubmission</c> and
/// <c>UA.Action.Freedom.ManifestWorker.Documents.ManifestDocumentRequest</c>. The
/// two projects deliberately do not share a type — the worker is a separate deployable and in
/// the target design an Azure Function — so this is a contract between processes. It is written
/// with <see cref="JsonSerializerOptions.Web"/> because that is what the worker reads with, and
/// a component test pins the literal JSON rather than round-tripping through this serialiser,
/// which would only prove the code agrees with itself.
///
/// <paramref name="queues"/> is optional, matching how the health checks take their storage
/// clients: an application with no storage account configured still starts and still explains
/// itself on <c>/health/ready</c>, rather than failing to build its service provider. Approving
/// a manifest then fails with a message that says what is missing.
/// </remarks>
public sealed class AzureManifestWorkQueue(
    QueueServiceClient? queues,
    IOptions<StorageOptions> storage,
    IOptions<CustomsOptions> customs,
    IOptions<EloOptions> elo,
    QueueFlowMetrics? metrics = null) : IManifestWorkQueue
{
    private readonly StorageOptions _storage = storage.Value;
    private readonly CustomsOptions _customs = customs.Value;
    private readonly EloOptions _elo = elo.Value;

    public Task EnqueueGmrSubmissionAsync(
        GmrSubmissionRequest submission, CancellationToken cancellationToken) =>
        Enqueue(
            QueueNames.CustomsWork,
            _storage.CustomsQueue,
            "Storage:ConnectionString is not configured, so the Goods Movement Reference for this manifest "
            + "cannot be handed to the Customs Worker. Approving a manifest is the point of no return, so it "
            + "fails here rather than confirming a manifest whose paperwork will never be submitted.",
            new
            {
                manifestId = submission.ManifestId,
                haulierEori = _customs.HaulierEori,
                vehicleRegistration = submission.VehicleRegistration,
                routeId = _customs.RouteId,

                // HMRC wants a port-local departure with no offset and no seconds.
                localDateTimeOfDeparture = (submission.DepartsAt ?? DateTime.UtcNow)
                    .ToString("yyyy-MM-ddTHH:mm"),
            },
            cancellationToken);

    public Task EnqueueDocumentAsync(
        ManifestDocumentRequest document, CancellationToken cancellationToken) =>
        // The record serialises as-is: it already contains exactly what the document may show,
        // so there is no mapping step here that could add something it must not.
        Enqueue(
            QueueNames.ManifestDocuments,
            _storage.DocumentQueue,
            "Storage:ConnectionString is not configured, so the document for this manifest cannot be handed "
            + "to the Manifest Worker.",
            document,
            cancellationToken);

    public Task EnqueueEloEnvelopeAsync(
        EloEnvelopeRequest envelope, CancellationToken cancellationToken)
    {
        // Which declarations go on the envelope comes from configuration, the way the haulier EORI
        // and route do for a GMR — today it is an environment fact rather than a per-manifest one.
        // It stops being one as soon as Freedom obtains a real ENS from ICS2.
        var declarations = string.IsNullOrWhiteSpace(_elo.PlaceholderDeclarationIdentifier)
            ? Array.Empty<string>()
            : [_elo.PlaceholderDeclarationIdentifier];

        // ENV_CTR_RG08: a loaded transport unit must name at least one declaration. Refusing here
        // rather than paying a round trip to be told the same thing, and saying which setting is
        // missing — the manifest is already frozen by this point, so the message has to be useful.
        if (envelope.Profile.RequiresADeclaration && declarations.Length == 0)
        {
            throw new InvalidOperationException(
                $"{EloOptions.SectionName}:{nameof(EloOptions.PlaceholderDeclarationIdentifier)} is not "
                + "configured, and French customs refuses an envelope for a loaded lorry that names no "
                + "declaration (ENV_CTR_RG08). Set it, or integrate ICS2 so a real ENS is available.");
        }

        return Enqueue(
            QueueNames.EloEnvelopes,
            _storage.EloQueue,
            "Storage:ConnectionString is not configured, so the French customs logistics envelope for this "
            + "manifest cannot be handed to the Customs Worker. France requires one per transport unit at "
            + "the Smart Border, so this fails here rather than confirming a manifest whose vehicle cannot "
            + "cross.",
            new
            {
                manifestId = envelope.ManifestId,
                crossingDirection = envelope.Profile.Direction.ToString(),
                lorryType = envelope.Profile.LorryType.ToString(),
                tirAta = envelope.Profile.TirAta,
                hasTransportContract = envelope.Profile.HasTransportContract,
                postal = envelope.Profile.Postal,
                emptyPackaging = envelope.Profile.EmptyPackaging,
                sanitaryOrPhytosanitary = envelope.Profile.SanitaryOrPhytosanitary,
                fisheryProducts = envelope.Profile.FisheryProducts,
                declarationIdentifiers = declarations,
            },
            cancellationToken);
    }

    /// <summary>
    /// Sends one message inside a producer span, with that span's <c>traceparent</c> beside the
    /// payload so the worker's span can link back to the approval that caused it. Counted as ok or
    /// failed; the failure still propagates, because a message that was not queued must not be
    /// reported as queued.
    /// </summary>
    private async Task Enqueue(
        string label, string queueName, string notConfigured, object payload, CancellationToken cancellationToken)
    {
        using var producer = QueueTelemetry.StartProducer(label);

        try
        {
            if (queues is null)
            {
                throw new InvalidOperationException(notConfigured);
            }

            await queues.GetQueueClient(queueName).SendMessageAsync(
                QueueTelemetry.Serialize(payload, QueueTelemetry.CurrentTraceparent()), cancellationToken);

            metrics?.Enqueued(label, succeeded: true);
        }
        catch
        {
            producer?.SetStatus(ActivityStatusCode.Error);
            metrics?.Enqueued(label, succeeded: false);
            throw;
        }
    }
}

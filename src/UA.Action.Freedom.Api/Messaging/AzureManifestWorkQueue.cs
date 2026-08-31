using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Manifests;

namespace UA.Action.Freedom.Api.Messaging;

/// <summary>
/// Puts Goods Movement Reference submissions on the customs work queue for the Customs Worker
/// to pick up.
/// </summary>
/// <remarks>
/// Pull, not push: the worker drains this queue and polls HMRC for outcomes, and Freedom exposes
/// no inbound callback endpoint (docs/recommendations.md §4.1).
///
/// The wire shape must match <c>UA.Action.Freedom.CustomsWorker.Customs.GmrSubmission</c>. The
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
    IOptions<CustomsOptions> customs) : IManifestWorkQueue
{
    private readonly StorageOptions _storage = storage.Value;
    private readonly CustomsOptions _customs = customs.Value;

    public async Task EnqueueGmrSubmissionAsync(
        GmrSubmissionRequest submission, CancellationToken cancellationToken)
    {
        if (queues is null)
        {
            throw new InvalidOperationException(
                "Storage:ConnectionString is not configured, so the Goods Movement Reference for this manifest "
                + "cannot be handed to the Customs Worker. Approving a manifest is the point of no return, so it "
                + "fails here rather than confirming a manifest whose paperwork will never be submitted.");
        }

        var queue = queues.GetQueueClient(_storage.CustomsQueue);

        var message = JsonSerializer.Serialize(
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
            JsonSerializerOptions.Web);

        await queue.SendMessageAsync(message, cancellationToken);
    }
}

using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Declarations;

namespace UA.Action.Freedom.Api.Documents;

/// <summary>
/// Holds the detail of each recorded ICS2 Entry Summary Declaration in the <c>ens</c> container, at
/// <c>declarations/{declarationId}.json</c>.
/// </summary>
/// <remarks>
/// The only blob store the API writes as well as reads. The others are a worker's output read back
/// here; nothing submits an ENS, because ICS2's Shared Trader Interface speaks eDelivery AS4 and an
/// always-on inbound access point is what <c>docs/recommendations.md</c> §4.1 declines — so there is
/// no worker in this path and no queue (<c>docs/adr/0003</c>).
///
/// <para>
/// Keyed by the declaration, not the manifest: the declaration row says whether an ENS is accepted and
/// what its MRN is, and this blob holds who filed it and when. <see cref="SaveAsync"/> creates and will
/// not replace, and the service settles the race rather than a read-then-write here. A withdrawn
/// declaration keeps its blob, so its MRN stays as history.
/// </para>
///
/// <para>
/// <paramref name="blobs"/> is optional, matching the queue adapter and the health checks: an
/// application with no storage account configured still starts and still explains itself on
/// <c>/health/ready</c>.
/// </para>
/// </remarks>
public sealed class BlobEnsDeclarationStore(
    BlobServiceClient? blobs,
    IOptions<StorageOptions> storage) : IEnsDeclarationStore
{
    /// <summary>camelCase, so the stored shape matches every other wire shape in the solution.</summary>
    private static readonly JsonSerializerOptions DocumentFormat = JsonSerializerOptions.Web;

    private readonly StorageOptions _storage = storage.Value;

    public async Task<EnsDeclarationReadModel?> GetAsync(
        int declarationId, CancellationToken cancellationToken)
    {
        if (Current(declarationId) is not { } blob)
        {
            return null;
        }

        try
        {
            var content = await blob.DownloadContentAsync(cancellationToken);

            // A blob that is there but unreadable is not "no declaration". Let it throw and be a 500
            // with a traceId rather than report that nothing was recorded.
            return JsonSerializer.Deserialize<EnsDeclarationReadModel>(
                content.Value.Content.ToString(), DocumentFormat);
        }
        catch (RequestFailedException failed) when (failed.Status == 404)
        {
            // Nothing recorded yet, or the container does not exist because tofu has not run. Both
            // mean the same thing to a caller.
            return null;
        }
    }

    public async Task<bool> SaveAsync(
        EnsDeclarationReadModel declaration, CancellationToken cancellationToken)
    {
        if (Current(declaration.DeclarationId) is not { } blob)
        {
            throw new InvalidOperationException(
                "Storage:ConnectionString is not configured, so the ICS2 declaration for this vehicle "
                + "cannot be recorded. This fails here rather than reporting a declaration that was never stored.");
        }

        var json = JsonSerializer.Serialize(declaration, DocumentFormat);

        try
        {
            // IfNoneMatch = ETag.All is "create, do not replace": the blob equivalent of the conditional
            // UPDATE that makes the declaration's reference write-once. The service refuses the second
            // write, so two dispatchers recording different MRNs at once resolve to one declaration
            // instead of the last write silently winning.
            await blob.UploadAsync(
                new BinaryData(Encoding.UTF8.GetBytes(json)),
                new BlobUploadOptions
                {
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                },
                cancellationToken);

            return true;
        }
        catch (RequestFailedException failed)
            when (failed.Status == 409 || failed.ErrorCode == BlobErrorCode.BlobAlreadyExists)
        {
            return false;
        }
    }

    private BlobClient? Current(int declarationId) =>
        blobs?.GetBlobContainerClient(_storage.EnsContainer).GetBlobClient($"declarations/{declarationId}.json");
}

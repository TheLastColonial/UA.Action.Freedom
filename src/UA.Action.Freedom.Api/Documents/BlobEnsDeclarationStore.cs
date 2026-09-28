using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Manifests;

namespace UA.Action.Freedom.Api.Documents;

/// <summary>
/// Holds the ICS2 Entry Summary Declaration recorded for each manifest in the <c>ens</c> container.
/// </summary>
/// <remarks>
/// The only blob store the API writes as well as reads. The others are a worker's output read back
/// here; nothing submits an ENS, because ICS2's Shared Trader Interface speaks eDelivery AS4 and an
/// always-on inbound access point is what <c>docs/recommendations.md</c> §4.1 declines — so there is
/// no worker in this path and no queue (<c>docs/adr/0003</c>).
///
/// <para>
/// Blob rather than a column on <c>dbo.Manifest</c>, beside the envelope that ends up naming the MRN.
/// That costs the write-once guarantee a conditional <c>UPDATE</c> would have given, so it is bought
/// back at the storage layer instead: <see cref="SaveAsync"/> creates and will not replace, and the
/// service settles the race rather than a read-then-write here.
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
        string manifestId, CancellationToken cancellationToken)
    {
        if (Current(manifestId) is not { } blob)
        {
            return null;
        }

        try
        {
            var content = await blob.DownloadContentAsync(cancellationToken);

            // A blob that is there but unreadable is not "no declaration". Reporting none would let
            // a manifest be approved without one, which is the one wrong answer this method can
            // give. Let it throw and be a 500 with a traceId.
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
        if (Current(declaration.ManifestId) is not { } blob)
        {
            throw new InvalidOperationException(
                "Storage:ConnectionString is not configured, so the ICS2 declaration for this manifest "
                + "cannot be recorded. Approving a manifest needs one, so this fails here rather than "
                + "reporting a declaration that was never stored.");
        }

        var json = JsonSerializer.Serialize(declaration, DocumentFormat);

        try
        {
            // IfNoneMatch = ETag.All is "create, do not replace". This is the blob equivalent of the
            // conditional UPDATE that makes Manifest.GmrSubmittedAt write-once: the service refuses
            // the second write, so two dispatchers recording different MRNs at once resolve to one
            // declaration instead of the last write silently winning.
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

    public async Task<bool> SupersedeAsync(string manifestId, CancellationToken cancellationToken)
    {
        if (Current(manifestId) is not { } blob)
        {
            return false;
        }

        BinaryData existing;

        try
        {
            existing = (await blob.DownloadContentAsync(cancellationToken)).Value.Content;
        }
        catch (RequestFailedException failed) when (failed.Status == 404)
        {
            return false;
        }

        // Copy before deleting, the same ordering and the same reason as
        // AzureEloWorkQueue.DeadLetterAsync: if the process dies between the two the record still
        // exists, where the other order loses it outright. Several ENS fields are non-amendable, so
        // invalidate-and-refile is the normal correction path and the withdrawn MRN is what a
        // customs query months later will be about.
        //
        // Downloaded and re-uploaded rather than server-side copied: a copy needs a readable URI,
        // which on a private container means minting a SAS, and in Azure the account is reached by
        // managed identity with shared-key authorisation disabled (§4.2). A recorded declaration is
        // a few hundred bytes.
        var name = $"{manifestId}/superseded-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}.json";

        await Container()!.GetBlobClient(name).UploadAsync(
            existing,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
            },
            cancellationToken);

        await blob.DeleteIfExistsAsync(cancellationToken: cancellationToken);

        return true;
    }

    private BlobContainerClient? Container() =>
        blobs?.GetBlobContainerClient(_storage.EnsContainer);

    /// <summary>
    /// The manifest's current declaration. Superseded ones live under a <c>{manifestId}/</c> prefix,
    /// so they can never be mistaken for it.
    /// </summary>
    private BlobClient? Current(string manifestId) =>
        Container()?.GetBlobClient($"{manifestId}.json");
}

using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Manifests;

namespace UA.Action.Freedom.Api.Documents;

/// <summary>
/// Reads back the envelopes the Customs Worker wrote to the <c>elo</c> container.
/// </summary>
/// <remarks>
/// The read side of a hand-off the API only writes to, and the reason it goes through blob storage
/// rather than a table is that the worker has no database: it cannot read a Ukrainian delivery address
/// because it cannot read anything, and that is worth more than the convenience of a column.
///
/// <para>
/// Served through this authenticated API rather than as a blob URL. <c>docs/recommendations.md</c>
/// §4.3 requires document access to be time-boxed and never a link in an email; streaming through an
/// endpoint behind <c>manifests:read</c> is the same posture without the short-lived SAS machinery,
/// which is still to be built.
/// </para>
///
/// <para>
/// <paramref name="blobs"/> is optional, matching the queue adapter and the health checks: an
/// application with no storage account configured still starts and still explains itself on
/// <c>/health/ready</c>. Asking for an envelope then reads as "there is none", which is true.
/// </para>
/// </remarks>
public sealed class BlobEloEnvelopeStore(
    BlobServiceClient? blobs,
    IOptions<StorageOptions> storage) : IEloEnvelopeStore
{
    /// <summary>camelCase, because the worker wrote these with the same configuration.</summary>
    private static readonly JsonSerializerOptions DocumentFormat = JsonSerializerOptions.Web;

    private readonly StorageOptions _storage = storage.Value;

    public async Task<EloEnvelopeReadModel?> GetAsync(
        string manifestId, CancellationToken cancellationToken)
    {
        var json = await Download($"{manifestId}.json", cancellationToken);

        if (json is null)
        {
            return null;
        }

        // A blob that is there but unreadable is not "no envelope": that would report a vehicle as
        // having no paperwork when French customs has issued it some, which is the one wrong answer
        // this method can give. Let it throw and be a 500 with a traceId.
        return JsonSerializer.Deserialize<EloEnvelopeReadModel>(json, DocumentFormat);
    }

    public async Task<byte[]?> GetBarcodeAsync(string manifestId, CancellationToken cancellationToken) =>
        await Download($"{manifestId}.pdf", cancellationToken);

    private async Task<byte[]?> Download(string name, CancellationToken cancellationToken)
    {
        if (blobs is null)
        {
            return null;
        }

        var blob = blobs.GetBlobContainerClient(_storage.EloContainer).GetBlobClient(name);

        try
        {
            var content = await blob.DownloadContentAsync(cancellationToken);
            return content.Value.Content.ToArray();
        }
        catch (RequestFailedException failed) when (failed.Status == 404)
        {
            // Not an error: the worker has not got to this manifest yet, or the container does not
            // exist because tofu has not run. Both mean the same thing to a caller.
            return null;
        }
    }
}

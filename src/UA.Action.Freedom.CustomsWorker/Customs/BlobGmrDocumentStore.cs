using System.Text;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.CustomsWorker.Configuration;

namespace UA.Action.Freedom.CustomsWorker.Customs;

/// <summary>
/// Stores GMR documents in the <c>gmr/</c> prefix of the document storage account.
/// </summary>
public sealed class BlobGmrDocumentStore(
    BlobServiceClient blobs,
    IOptions<StorageOptions> options) : IGmrDocumentStore
{
    private readonly StorageOptions _storage = options.Value;

    public async Task SaveAsync(string gmrId, string content, CancellationToken cancellationToken)
    {
        var blob = blobs
            .GetBlobContainerClient(_storage.GmrContainer)
            .GetBlobClient($"{gmrId}.json");

        using var payload = new MemoryStream(Encoding.UTF8.GetBytes(content));

        // Overwrite: the latest outcome for a GMR is the one that counts. Blob versioning
        // and soft delete keep the earlier ones (recommendations 4.3), so nothing is lost
        // and the container is never the place a stale status hides.
        await blob.UploadAsync(payload, overwrite: true, cancellationToken);
    }
}

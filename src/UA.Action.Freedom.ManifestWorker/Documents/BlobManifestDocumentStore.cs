using System.Text;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.ManifestWorker.Configuration;

namespace UA.Action.Freedom.ManifestWorker.Documents;

/// <summary>
/// Writes manifest documents to the <c>manifests</c> container.
/// </summary>
/// <remarks>
/// Public blob access is off at the account level; documents are served through short-lived
/// user-delegation SAS after the application has authorised the reader, and a document URL never
/// goes in an email (recommendations 4.3).
/// </remarks>
public sealed class BlobManifestDocumentStore(
    BlobServiceClient blobs,
    IOptions<StorageOptions> options) : IManifestDocumentStore
{
    private readonly StorageOptions _storage = options.Value;

    public async Task SaveAsync(string manifestId, string content, CancellationToken cancellationToken)
    {
        var blob = blobs.GetBlobContainerClient(_storage.DocumentsContainer).GetBlobClient($"{manifestId}.txt");
        using var payload = new MemoryStream(Encoding.UTF8.GetBytes(content));

        // Overwrite: the latest document for a manifest is the one that counts. Blob versioning
        // and soft delete keep the earlier ones (4.3), which covers the realistic failure —
        // somebody regenerates the wrong manifest the night before departure.
        await blob.UploadAsync(payload, overwrite: true, cancellationToken);
    }
}

using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.CustomsWorker.Configuration;

namespace UA.Action.Freedom.CustomsWorker.Elo;

/// <summary>
/// Stores issued envelopes and their barcode documents in the <c>elo/</c> container.
/// </summary>
/// <remarks>
/// Two blobs per manifest rather than the barcode base64-encoded inside the JSON: the API serves the
/// PDF straight to a browser, and a document that has to be decoded out of a JSON field first cannot
/// be streamed. The blob names are the manifest reference, which is what the API looks one up by.
/// </remarks>
public sealed class BlobEloDocumentStore(
    BlobServiceClient blobs,
    IOptions<StorageOptions> options) : IEloDocumentStore
{
    /// <summary>
    /// camelCase, because the API reads these blobs back and serves them as its own JSON.
    /// </summary>
    private static readonly JsonSerializerOptions DocumentFormat = JsonSerializerOptions.Web;

    private readonly StorageOptions _storage = options.Value;

    public async Task SaveAsync(
        EloEnvelopeDocument envelope, byte[]? barcodePdf, CancellationToken cancellationToken)
    {
        var container = blobs.GetBlobContainerClient(_storage.EloContainer);

        // The barcode first, then the reference. The reference is what the API reports as "there is
        // an envelope", and it carries HasBarcodeDocument — so writing it last means it never
        // promises a document that is not there yet.
        if (barcodePdf is not null)
        {
            using var pdf = new MemoryStream(barcodePdf);
            await container.GetBlobClient($"{envelope.ManifestId}.pdf").UploadAsync(
                pdf,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/pdf" },
                },
                cancellationToken);
        }

        using var payload = new MemoryStream(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, DocumentFormat)));

        // Overwrite: the latest state of an envelope is the one that counts. Blob versioning and
        // soft delete keep the earlier ones (recommendations 4.3).
        await container.GetBlobClient($"{envelope.ManifestId}.json").UploadAsync(
            payload,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
            },
            cancellationToken);
    }
}

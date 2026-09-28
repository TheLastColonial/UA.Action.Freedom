using UA.Action.Freedom.Application.Manifests;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Stands in for the <c>elo</c> container the Customs Worker writes to.
/// </summary>
/// <remarks>
/// Empty by default, which is what an approved manifest looks like in the seconds before the worker
/// picks its envelope request off the queue — and for ever, if the hand-off failed. Both are a 404,
/// and a fake that invented an envelope would hide the difference between "not yet" and "never".
///
/// <para>
/// It answers a missing barcode the way blob storage does: a 404 on the blob rather than an error, so
/// an envelope stored without a usable PDF reads as "no document", not as a broken endpoint.
/// </para>
/// </remarks>
internal sealed class InMemoryEloEnvelopeStore : IEloEnvelopeStore
{
    private readonly Dictionary<string, (EloEnvelopeReadModel Envelope, byte[]? Barcode)> _envelopes = [];

    internal InMemoryEloEnvelopeStore With(EloEnvelopeReadModel envelope, byte[]? barcode = null)
    {
        _envelopes[envelope.ManifestId] = (envelope, barcode);
        return this;
    }

    public Task<EloEnvelopeReadModel?> GetAsync(string manifestId, CancellationToken cancellationToken) =>
        Task.FromResult(_envelopes.TryGetValue(manifestId, out var stored) ? stored.Envelope : null);

    public Task<byte[]?> GetBarcodeAsync(string manifestId, CancellationToken cancellationToken) =>
        Task.FromResult(_envelopes.TryGetValue(manifestId, out var stored) ? stored.Barcode : null);
}

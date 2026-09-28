using UA.Action.Freedom.Application.Manifests;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Stands in for the <c>ens</c> container the API records ICS2 declarations in.
/// </summary>
/// <remarks>
/// Empty by default, which is what a manifest looks like before anyone has filed its Entry Summary
/// Declaration — and the state in which approving it is refused.
///
/// <para>
/// A fake must enforce every rule its real adapter does, or tests pass against behaviour production
/// does not have. Two rules matter here, and both are enforced by storage rather than by the handler,
/// so a lenient fake would hide them:
/// </para>
/// <list type="bullet">
/// <item>
/// <see cref="SaveAsync"/> creates and never replaces, returning <see langword="false"/> instead —
/// <c>BlobEnsDeclarationStore</c> gets that from a conditional create
/// (<c>IfNoneMatch = ETag.All</c>), which is the blob equivalent of the conditional <c>UPDATE</c>
/// that makes <c>Manifest.GmrSubmittedAt</c> write-once.
/// </item>
/// <item>
/// <see cref="SupersedeAsync"/> keeps what it set aside rather than discarding it, so
/// <see cref="Superseded"/> can be asserted the way the real store's <c>{manifestId}/</c> prefix can
/// be listed.
/// </item>
/// </list>
/// </remarks>
internal sealed class InMemoryEnsDeclarationStore : IEnsDeclarationStore
{
    private readonly Dictionary<string, EnsDeclarationReadModel> _current = [];
    private readonly List<EnsDeclarationReadModel> _superseded = [];

    /// <summary>The declarations set aside by <see cref="SupersedeAsync"/>, oldest first.</summary>
    internal IReadOnlyList<EnsDeclarationReadModel> Superseded => _superseded;

    /// <summary>Seeds a recorded declaration, bypassing the write-once rule the API goes through.</summary>
    internal InMemoryEnsDeclarationStore With(EnsDeclarationReadModel declaration)
    {
        _current[declaration.ManifestId] = declaration;
        return this;
    }

    /// <summary>Seeds the declaration a manifest needs before it can be approved.</summary>
    internal InMemoryEnsDeclarationStore With(string manifestId, string mrn = "25FR17551780961AT5") =>
        With(new EnsDeclarationReadModel(
            manifestId, mrn, new DateTimeOffset(2026, 8, 24, 9, 30, 0, TimeSpan.Zero), "groundofficer", null));

    public Task<EnsDeclarationReadModel?> GetAsync(string manifestId, CancellationToken cancellationToken) =>
        Task.FromResult(_current.TryGetValue(manifestId, out var stored) ? stored : null);

    public Task<bool> SaveAsync(EnsDeclarationReadModel declaration, CancellationToken cancellationToken)
    {
        // Create, not replace: TryAdd is this fake's conditional create.
        return Task.FromResult(_current.TryAdd(declaration.ManifestId, declaration));
    }

    public Task<bool> SupersedeAsync(string manifestId, CancellationToken cancellationToken)
    {
        if (!_current.Remove(manifestId, out var withdrawn))
        {
            return Task.FromResult(false);
        }

        // Kept, not discarded: a customs query months later is about the MRN that was filed at the
        // time, which may well be one that was withdrawn.
        _superseded.Add(withdrawn);

        return Task.FromResult(true);
    }
}

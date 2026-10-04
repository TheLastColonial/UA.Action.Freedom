using UA.Action.Freedom.Application.Declarations;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Stands in for the <c>ens</c> container the API records the detail of ICS2 declarations in, keyed by
/// declaration. <see cref="SaveAsync"/> creates and never replaces, returning <see langword="false"/>
/// instead — <c>BlobEnsDeclarationStore</c> gets that from a conditional create
/// (<c>IfNoneMatch = ETag.All</c>), so a lenient fake would hide the write-once rule.
/// </summary>
internal sealed class InMemoryEnsDeclarationStore : IEnsDeclarationStore
{
    private readonly Dictionary<int, EnsDeclarationReadModel> _stored = [];

    public Task<EnsDeclarationReadModel?> GetAsync(int declarationId, CancellationToken cancellationToken) =>
        Task.FromResult(_stored.TryGetValue(declarationId, out var stored) ? stored : null);

    public Task<bool> SaveAsync(EnsDeclarationReadModel declaration, CancellationToken cancellationToken) =>
        Task.FromResult(_stored.TryAdd(declaration.DeclarationId, declaration));
}

using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Boxes;

/// <summary>Issue a QR label for a box, replacing any it already has.</summary>
public sealed record IssueBoxQrCodeCommand(int BoxId);

/// <summary>
/// Issuing a label is not a box edit, so it is allowed whatever state the box is in — a
/// validated box that lost its label in transit still needs a new one printed.
/// </summary>
/// <remarks>
/// Re-issuing revokes the previous code. The handler makes a single call: the revoke and the
/// new insert are one act, settled together in <see cref="IBoxRepository.IssueQrCodeAsync"/>.
/// </remarks>
public sealed class IssueBoxQrCodeHandler(IBoxRepository repository)
    : ICommandHandler<IssueBoxQrCodeCommand, BoxQrCodeReadModel?>
{
    public async Task<BoxQrCodeReadModel?> HandleAsync(
        IssueBoxQrCodeCommand command, CancellationToken cancellationToken)
    {
        if (!await repository.ExistsAsync(command.BoxId, cancellationToken))
        {
            return null;
        }

        return await repository.IssueQrCodeAsync(
            command.BoxId, Guid.NewGuid(), DateTime.UtcNow, cancellationToken);
    }
}

/// <summary>Revoke a box's QR label without issuing another — the box is being retired.</summary>
public sealed record RevokeBoxQrCodeCommand(int BoxId);

public enum RevokeBoxQrCodeOutcome
{
    Revoked,
    NotFound
}

/// <summary>
/// <see cref="RevokeBoxQrCodeOutcome.NotFound"/> covers both "no such box" and "the box has no
/// active label": from the caller's side there is nothing to revoke either way.
/// </summary>
public sealed class RevokeBoxQrCodeHandler(IBoxRepository repository)
    : ICommandHandler<RevokeBoxQrCodeCommand, RevokeBoxQrCodeOutcome>
{
    public async Task<RevokeBoxQrCodeOutcome> HandleAsync(
        RevokeBoxQrCodeCommand command, CancellationToken cancellationToken)
        => await repository.RevokeActiveQrCodeAsync(command.BoxId, cancellationToken)
            ? RevokeBoxQrCodeOutcome.Revoked
            : RevokeBoxQrCodeOutcome.NotFound;
}

/// <summary>The box's active QR label, or <c>null</c> if it has none.</summary>
public sealed record GetBoxQrCodeQuery(int BoxId);

public sealed class GetBoxQrCodeHandler(IBoxRepository repository)
    : IQueryHandler<GetBoxQrCodeQuery, BoxQrCodeReadModel?>
{
    public Task<BoxQrCodeReadModel?> HandleAsync(GetBoxQrCodeQuery query, CancellationToken cancellationToken)
        => repository.GetActiveQrCodeAsync(query.BoxId, cancellationToken);
}

/// <summary>
/// Resolve a scanned token to the box it identifies, or <c>null</c> when the token is unknown or
/// its code has been revoked.
/// </summary>
public sealed record ResolveBoxByQrCodeQuery(Guid Token);

public sealed class ResolveBoxByQrCodeHandler(IBoxRepository repository)
    : IQueryHandler<ResolveBoxByQrCodeQuery, BoxReadModel?>
{
    public async Task<BoxReadModel?> HandleAsync(
        ResolveBoxByQrCodeQuery query, CancellationToken cancellationToken)
    {
        var code = await repository.ResolveActiveQrCodeAsync(query.Token, cancellationToken);

        return code is null
            ? null
            : await repository.GetByIdAsync(code.BoxId, cancellationToken);
    }
}

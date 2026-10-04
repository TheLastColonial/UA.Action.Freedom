using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Declarations;

/// <summary>
/// A declaration as the API shows it. Never carries the authority's free text: a refusal is a bounded
/// <see cref="ReasonCode"/> only.
/// </summary>
public sealed record DeclarationReadModel(
    int Id,
    int ConvoyId,
    string Vin,
    DeclarationKind Kind,
    DeclarationStatus Status,
    Guid? ReceiverRef,
    string? Reference,
    string? ReasonCode,
    string? RecordedByName,
    DateTime? RecordedAt,
    string? LastChangedByName,
    DateTime? LastChangedAt);

public enum RecordReferenceResult
{
    Recorded,
    VehicleNotOnConvoy,
    AlreadyRecorded,
}

/// <summary>Persistence port for <c>dbo.Declaration</c>.</summary>
/// <remarks>
/// Every status change is a conditional <c>UPDATE ... WHERE Status = @from</c>, so the database settles a
/// race between two dispatchers, and a reference is written only by the transition that files it.
/// </remarks>
public interface IDeclarationRepository
{
    /// <summary>Every declaration for the vehicle on the convoy, withdrawn ones included (they are the history).</summary>
    Task<IReadOnlyList<DeclarationReadModel>> ListAsync(int convoyId, string vin, CancellationToken cancellationToken);

    /// <summary>The one declaration of that kind (and receiver) that is not withdrawn, or null.</summary>
    Task<DeclarationReadModel?> GetCurrentAsync(
        int convoyId, string vin, DeclarationKind kind, Guid? receiverRef, CancellationToken cancellationToken);

    /// <summary>
    /// Records the authority's reference, creating the declaration if none is current, and moves it to
    /// its recorded status. Write-once: a declaration that already carries a reference is not replaced.
    /// A refused declaration is treated as corrected and recorded afresh.
    /// </summary>
    Task<RecordReferenceResult> RecordReferenceAsync(
        int convoyId, string vin, DeclarationKind kind, Guid? receiverRef, string? reference,
        CancellationToken cancellationToken);

    /// <summary>Filed to Refused with a bounded reason code. False when it was not filed.</summary>
    Task<bool> RefuseAsync(int id, string reasonCode, CancellationToken cancellationToken);

    /// <summary>
    /// Invalidates an accepted or filed declaration so a replacement can be filed: it goes stale and is
    /// withdrawn in one transaction, and the record and its reference are kept. False when none was current.
    /// </summary>
    Task<bool> WithdrawAsync(int convoyId, string vin, DeclarationKind kind, Guid? receiverRef, CancellationToken cancellationToken);
}

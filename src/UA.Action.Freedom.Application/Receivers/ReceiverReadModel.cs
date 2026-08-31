namespace UA.Action.Freedom.Application.Receivers;

/// <summary>
/// A receiver as the rest of the application sees it: an opaque reference, the organisation,
/// and a region.
/// </summary>
/// <remarks>
/// <strong>There is deliberately no address or contact on this type.</strong> This is the shape
/// that may appear on a manifest, in a border-guard view, or anywhere else that crosses a
/// border — region-level is as precise as anything that travels gets
/// (docs/domain/key-concepts.md § Data Sensitivity, recommendations §4.4.2). The full detail
/// lives in <see cref="ReceiverDetailReadModel"/>, behind a different database identity.
///
/// Keeping the two apart as separate types is what makes the redaction structural: code that
/// only has a <see cref="ReceiverReadModel"/> has nothing sensitive to leak, so a document
/// generator or a log statement cannot disclose an address by accident.
/// </remarks>
public sealed record ReceiverReadModel(Guid Ref, string Organisation, string Region);

/// <summary>
/// The delivery detail for a receiver: where the aid actually goes and who signs for it.
/// </summary>
/// <remarks>
/// The highest-risk data in the system. A manifest listing precise delivery addresses is a
/// targeting document, and it crosses several borders where it may be inspected, photographed
/// or seized. Reachable only through the Ground Officer role and the <c>ground_officer</c>
/// database identity, and every read is audited.
///
/// <see cref="DeleteAfter"/> carries the retention decision from §4.4.5 — delivery detail is
/// removed a defined period after delivery is confirmed, because data you no longer hold cannot
/// be disclosed. Nothing enforces that sweep yet; it wants a timer-triggered job.
/// </remarks>
public sealed record ReceiverDetailReadModel(
    Guid Ref,
    string ContactName,
    string ContactPhone,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? PostCode,
    DateTime? DeleteAfter);

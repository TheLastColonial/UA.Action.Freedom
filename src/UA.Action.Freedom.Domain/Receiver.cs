namespace UA.Action.Freedom.Domain;

/// <summary>
/// Ultimate destination of a <see cref="Box"/>'s contents in Ukraine
/// </summary>
/// <remarks>
/// The highest-risk data in the system. <see cref="Address"/> and
/// <see cref="ResponsibleIndividual"/> are Ground Officer only, held in the segregated
/// <c>sensitive</c> schema and redacted from anything that travels — a manifest listing precise
/// delivery addresses is a targeting document. Everything else here is what the rest of the
/// application may join on. See docs/domain/key-concepts.md § Data Sensitivity.
/// </remarks>
public class Receiver
{
    /// <summary>
    /// The opaque reference the rest of the application joins on. Carries no addressing detail.
    /// </summary>
    public required ReceiverRef Ref { get; set; }

    public required string Organisation { get; set; }

    /// <summary>
    /// Region-level destination — as precise as anything that crosses a border gets.
    /// </summary>
    public required string Region { get; set; }

    /// <summary>
    /// Whether this Receiver may be sent to. Set by an Administrator only. Not sensitive: it records
    /// that the Receiver may be sent to, never what kind of body it is (decision D33).
    /// </summary>
    public ReceiverStatus Status { get; set; } = ReceiverRegistration.Initial;

    /// <summary>
    /// Full delivery address. Null unless a Ground Officer resolved it, and every such read is audited.
    /// </summary>
    public Address? Address { get; set; }

    /// <summary>
    /// Delivery contact. Null unless a Ground Officer resolved it, and every such read is audited.
    /// </summary>
    public Person? ResponsibleIndividual { get; set; }
}

/// <summary>
/// Unique reference to a <see cref="Receiver"/>
/// </summary>
/// <param name="Value"></param>
public record ReceiverRef(Guid Value);

/// <summary>
/// Whether a Receiver may be sent to. The values are stored, so do not renumber them.
/// </summary>
/// <remarks>
/// This records only that an Administrator has authorised sending to the Receiver. It deliberately
/// has no companion field for why, or for what kind of body the Receiver is: that is not for this
/// software to know (ADR 0012, decision D33).
/// </remarks>
public enum ReceiverStatus
{
    Pending = 0,
    Registered = 1,
    Suspended = 2,
    Expired = 3,
}

/// <summary>The rules about a Receiver's registration status.</summary>
public static class ReceiverRegistration
{
    /// <summary>A Receiver is pending until an Administrator registers it.</summary>
    public const ReceiverStatus Initial = ReceiverStatus.Pending;

    /// <summary>Only a registered Receiver can be a box's destination or a vehicle's handover Receiver.</summary>
    public static bool CanReceive(ReceiverStatus status) => status == ReceiverStatus.Registered;
}

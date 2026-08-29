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

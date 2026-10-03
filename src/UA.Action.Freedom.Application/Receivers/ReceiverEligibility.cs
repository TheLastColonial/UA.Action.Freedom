using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Receivers;

/// <summary>Whether a receiver may be named as a destination.</summary>
public enum ReceiverEligibility
{
    Eligible,
    NotFound,
    NotRegistered
}

/// <summary>
/// The one check behind every place a receiver is named: a box's destination, a vehicle's handover
/// receiver. It has to exist, and it has to be registered (ADR 0012, decisions D30 and D35).
/// </summary>
public static class ReceiverEligibilityCheck
{
    public static async Task<ReceiverEligibility> CheckAsync(
        IReceiverRepository receivers, Guid receiverRef, CancellationToken cancellationToken)
    {
        var receiver = await receivers.GetByRefAsync(receiverRef, cancellationToken);

        if (receiver is null)
        {
            return ReceiverEligibility.NotFound;
        }

        return ReceiverRegistration.CanReceive(receiver.Status)
            ? ReceiverEligibility.Eligible
            : ReceiverEligibility.NotRegistered;
    }
}

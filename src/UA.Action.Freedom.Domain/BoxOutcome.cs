namespace UA.Action.Freedom.Domain;

/// <summary>
/// How a box's journey on one convoy ended (ADR 0015). "Lost" is not an outcome: a lost box is
/// <see cref="Undeliverable"/> with that as the reason.
/// </summary>
/// <remarks>The numbers are stored (<c>dbo.BoxOutcome.Outcome</c>, CHECK 0 to 4): do not reorder.</remarks>
public enum BoxOutcome
{
    Delivered = 0,
    Accepted = 1,
    Seized = 2,
    ReturnedToHub = 3,
    Undeliverable = 4,
}

public enum BoxOutcomeCheck
{
    Ok,
    ReasonRequired,
    HubNotRegistered,
}

public static class BoxOutcomes
{
    /// <summary>
    /// The reasons an undeliverable box may carry. A bounded set, never free text, because free
    /// text about where a box went missing can quote an address.
    /// </summary>
    public static readonly IReadOnlyList<string> UndeliverableReasons = ["damaged", "stolen", "other"];

    private static readonly HashSet<(BoxOutcome? From, BoxOutcome To)> Allowed =
    [
        (null, BoxOutcome.Delivered),
        (null, BoxOutcome.Seized),
        (null, BoxOutcome.ReturnedToHub),
        (null, BoxOutcome.Undeliverable),
        (BoxOutcome.Delivered, BoxOutcome.Accepted),
        (BoxOutcome.Delivered, BoxOutcome.Seized),
        (BoxOutcome.Delivered, BoxOutcome.ReturnedToHub),
        (BoxOutcome.Delivered, BoxOutcome.Undeliverable),
    ];

    /// <summary>Whether a box whose current outcome is <paramref name="from"/> (null: none yet) may take <paramref name="to"/>.</summary>
    public static bool CanRecord(BoxOutcome? from, BoxOutcome to) => Allowed.Contains((from, to));

    /// <summary>The rules on the outcome's own detail, apart from the edge.</summary>
    public static BoxOutcomeCheck Check(BoxOutcome outcome, string? reason, bool hubIsRegistered) => outcome switch
    {
        BoxOutcome.Undeliverable when reason is null || !UndeliverableReasons.Contains(reason) =>
            BoxOutcomeCheck.ReasonRequired,
        BoxOutcome.ReturnedToHub when !hubIsRegistered => BoxOutcomeCheck.HubNotRegistered,
        _ => BoxOutcomeCheck.Ok,
    };

    /// <summary>
    /// The one definition of "delivered": the closing report and any value report must both use it,
    /// so a seized or undeliverable box is never counted as value delivered.
    /// </summary>
    public static bool CountsAsDelivered(BoxOutcome outcome) =>
        outcome is BoxOutcome.Delivered or BoxOutcome.Accepted;

    public static decimal DeliveredValue(IEnumerable<(BoxOutcome? Outcome, decimal ValueGbp)> boxes) =>
        boxes.Where(box => box.Outcome is { } outcome && CountsAsDelivered(outcome)).Sum(box => box.ValueGbp);
}

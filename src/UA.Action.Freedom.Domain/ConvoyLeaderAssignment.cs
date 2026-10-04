namespace UA.Action.Freedom.Domain;

/// <summary>
/// A volunteer leading a convoy from <see cref="From"/> until <see cref="Until"/>. There is one open assignment (no
/// <see cref="Until"/>) per convoy; a reassignment closes it and opens another, so the history is kept (D8, D17).
/// </summary>
public sealed record ConvoyLeaderAssignment(int ConvoyId, Guid PersonId, DateTime From, DateTime? Until = null)
{
    public bool IsOpen => Until is null;
}

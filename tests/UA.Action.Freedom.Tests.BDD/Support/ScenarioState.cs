namespace UA.Action.Freedom.Tests.BDD.Support;

/// <summary>Mutable per-scenario state shared between step definitions and hooks.</summary>
public sealed class ScenarioState
{
    public string? CurrentToken { get; set; }

    /// <summary>VINs created during the scenario, removed again in the AfterScenario hook.</summary>
    public HashSet<string> CreatedVins { get; } = new(StringComparer.OrdinalIgnoreCase);
}

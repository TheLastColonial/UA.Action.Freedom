namespace UA.Action.Freedom.Tests.BDD.Support;

/// <summary>Mutable per-scenario state shared between step definitions and hooks.</summary>
public sealed class ScenarioState
{
    public string? CurrentToken { get; set; }

    /// <summary>
    /// Resources created during the scenario as (collection route, key) — for example
    /// <c>("vehicles", "WDB9066331S0BDD01")</c> — removed again in the AfterScenario hook.
    /// </summary>
    /// <remarks>
    /// Keyed by route rather than by type so that a new feature needs no new cleanup code: the
    /// hook deletes <c>/{resource}/{key}</c> and every slice in this API answers that shape.
    /// </remarks>
    public HashSet<(string Resource, string Key)> CreatedResources { get; } = [];
}

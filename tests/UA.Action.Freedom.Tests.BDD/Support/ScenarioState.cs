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

    /// <summary>
    /// The key of the most recently created resource, taken from its Location header. Scenarios
    /// write <c>{id}</c> in a path to mean "the thing I just made" — which is the only way to
    /// address a resource whose identifier the server mints.
    /// </summary>
    public string? LastCreatedKey { get; set; }

    private readonly Dictionary<string, string> remembered = [];

    /// <summary>
    /// Pins the most recently created key under a name, so a scenario that goes on to create
    /// something else can still address the earlier resource.
    /// </summary>
    public void Remember(string name) =>
        remembered[name] = LastCreatedKey
            ?? throw new InvalidOperationException($"Nothing has been created to remember as '{name}'.");

    /// <summary>
    /// Pins an arbitrary value under a name — an identifier a scenario needs later that did not
    /// come from the last Location header, such as the volunteer who validates a box.
    /// </summary>
    public void Pin(string name, string value) => remembered[name] = value;

    /// <summary>Reads back a value stored by <see cref="Pin"/> or <see cref="Remember"/>.</summary>
    public string Pinned(string name) =>
        remembered.TryGetValue(name, out var value)
            ? value
            : throw new InvalidOperationException($"No '{name}' has been remembered.");

    /// <summary>Substitutes <c>{id}</c> in <paramref name="template"/> with a remembered key.</summary>
    public string Recall(string name, string template) =>
        template.Replace(
            "{id}",
            remembered.TryGetValue(name, out var key)
                ? key
                : throw new InvalidOperationException($"No '{name}' has been remembered."),
            StringComparison.Ordinal);
}

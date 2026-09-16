using Reqnroll;

namespace UA.Action.Freedom.Tests.BDD.Support;

[Binding]
public sealed class CleanupHooks(FreedomApiClient api, ScenarioState state)
{
    /// <summary>
    /// Removes anything a scenario created, so a re-run starts clean. Best effort — a failure
    /// here must not mask the scenario result.
    /// </summary>
    /// <remarks>
    /// Deletes as <c>admin</c> by default, which holds every write policy that exists — with one
    /// deliberate exception. Removing a receiver also removes its Ukrainian delivery address, so
    /// that route is Ground Officer only and an admin token is refused; receivers are therefore
    /// cleaned up as <c>groundofficer</c>. A cleanup hook silently 403-ing is worse than one that
    /// fails loudly, because it leaves delivery detail behind (docs/recommendations.md §4.4).
    /// </remarks>
    [AfterScenario]
    public async Task RemoveResourcesCreatedByTheScenario()
    {
        if (state.CreatedResources.Count == 0)
        {
            return;
        }

        // Boxes before everything else: a box's bay assignment carries a foreign key to
        // dbo.Bay that is NO ACTION (bay history is not casually deleted, docs/domain/
        // key-concepts.md § Bay), so a location's bays cannot be removed by cascade while a
        // box still references one. Deleting the box first clears that reference.
        foreach (var (resource, key) in state.CreatedResources.OrderBy(r => r.Resource == "boxes" ? 0 : 1))
        {
            try
            {
                var token = await api.TokenForAsync(CleanerFor(resource));
                await api.SendAsync(HttpMethod.Delete, $"/{resource}/{key}", token, null);
            }
            catch
            {
                // best effort
            }
        }
    }

    /// <summary>
    /// The seed login that may delete this kind of resource. Everything is the Administrator's
    /// to remove except a receiver, whose deletion reaches into the sensitive schema.
    /// </summary>
    private static string CleanerFor(string resource) =>
        resource.Equals("receivers", StringComparison.OrdinalIgnoreCase) ? "groundofficer" : "admin";
}

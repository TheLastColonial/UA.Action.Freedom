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

        foreach (var (resource, key) in state.CreatedResources)
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

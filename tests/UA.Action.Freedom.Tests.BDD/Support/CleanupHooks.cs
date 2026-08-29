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
    /// Deletes as <c>admin</c>, which holds every write policy. A scenario that proved a role
    /// may <em>not</em> write has nothing to clean up anyway, and one that created a resource
    /// should not also depend on its own role being able to remove it.
    /// </remarks>
    [AfterScenario]
    public async Task RemoveResourcesCreatedByTheScenario()
    {
        if (state.CreatedResources.Count == 0)
        {
            return;
        }

        string adminToken;
        try
        {
            adminToken = await api.TokenForAsync("admin");
        }
        catch
        {
            return;
        }

        foreach (var (resource, key) in state.CreatedResources)
        {
            try
            {
                await api.SendAsync(HttpMethod.Delete, $"/{resource}/{key}", adminToken, null);
            }
            catch
            {
                // best effort
            }
        }
    }
}

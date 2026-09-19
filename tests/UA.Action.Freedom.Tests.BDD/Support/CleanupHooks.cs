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

        foreach (var (resource, key) in state.CreatedResources.OrderBy(r => DeletionOrder(r.Resource)))
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
    /// Things that name other things go first. A box's bay assignment carries a NO ACTION foreign
    /// key to dbo.Bay (bay history is not casually deleted, docs/domain/key-concepts.md § Bay), so
    /// a location cannot go while a box still references one of its bays. A vehicle's crew rows
    /// name a volunteer, and a volunteer still named anywhere is refused deletion with a 409 —
    /// deleting the vehicle cascades its crew away. Volunteers therefore go last.
    /// </summary>
    private static int DeletionOrder(string resource) => resource switch
    {
        "boxes" => 0,
        "vehicles" => 1,
        "people" => 3,
        _ => 2,
    };

    /// <summary>
    /// The seed login that may delete this kind of resource. Everything is the Administrator's
    /// to remove except a receiver, whose deletion reaches into the sensitive schema.
    /// </summary>
    private static string CleanerFor(string resource) =>
        resource.Equals("receivers", StringComparison.OrdinalIgnoreCase) ? "groundofficer" : "admin";
}

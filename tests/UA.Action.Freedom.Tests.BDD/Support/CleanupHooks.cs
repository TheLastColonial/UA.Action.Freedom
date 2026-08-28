using Reqnroll;

namespace UA.Action.Freedom.Tests.BDD.Support;

[Binding]
public sealed class CleanupHooks(FreedomApiClient api, ScenarioState state)
{
    /// <summary>
    /// Removes any vehicle a scenario created, so a re-run starts clean. Best effort — a
    /// failure here must not mask the scenario result.
    /// </summary>
    [AfterScenario]
    public async Task RemoveVehiclesCreatedByTheScenario()
    {
        if (state.CreatedVins.Count == 0)
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

        foreach (var vin in state.CreatedVins)
        {
            try
            {
                await api.SendAsync(HttpMethod.Delete, $"/vehicles/{vin}", adminToken, null);
            }
            catch
            {
                // best effort
            }
        }
    }
}

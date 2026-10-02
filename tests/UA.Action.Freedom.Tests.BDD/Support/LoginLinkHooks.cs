using System.Net;
using System.Text.Json;
using Reqnroll;

namespace UA.Action.Freedom.Tests.BDD.Support;

/// <summary>
/// Links each seed login to a volunteer, once per run, so scenarios that validate or shelve a
/// box (signed as the caller) have a caller who can sign.
/// </summary>
/// <remarks>
/// Keycloak generates a subject per user when the realm is imported, so the subjects change
/// whenever the realm is recreated and nothing can be seeded up front. The hook therefore reads
/// each login's subject from <c>GET /me</c> on every run and links it with the Administrator's
/// token, reusing the volunteer a previous run made for that login when there is one. The
/// volunteers it makes are deliberately not registered for cleanup: they are the identities the
/// seed logins sign as. It is best effort — when the stack is down, or the running image
/// predates <c>/me</c>, scenarios skip themselves.
/// </remarks>
[Binding]
public static class LoginLinkHooks
{
    private static readonly string[] SeedLogins = ["admin", "operator", "groundofficer"];

    private const string VolunteerFirstName = "BDD";

    [BeforeTestRun]
    public static async Task LinkSeedLoginsAsync()
    {
        try
        {
            using var api = new FreedomApiClient();
            var admin = await api.TokenForAsync("admin");

            foreach (var login in SeedLogins)
            {
                await LinkAsync(api, admin, login);
            }
        }
        catch
        {
            // The stack is down or predates /me: the scenarios skip themselves.
        }
    }

    private static async Task LinkAsync(FreedomApiClient api, string adminToken, string login)
    {
        var token = await api.TokenForAsync(login);
        var me = await api.SendAsync(HttpMethod.Get, "/me", token, null);
        if (me.StatusCode != HttpStatusCode.OK)
        {
            return;
        }

        var profile = JsonDocument.Parse(api.LastBody).RootElement;
        if (profile.GetProperty("personId").ValueKind != JsonValueKind.Null)
        {
            return;
        }

        var subject = profile.GetProperty("subject").GetString()!;
        var personId = await FindOrCreateVolunteerAsync(api, adminToken, login);

        await api.SendAsync(
            HttpMethod.Put, $"/people/{personId}/login", adminToken, JsonSerializer.Serialize(new { subject }));
    }

    private static async Task<string> FindOrCreateVolunteerAsync(FreedomApiClient api, string adminToken, string login)
    {
        await api.SendAsync(HttpMethod.Get, "/people?pageSize=200", adminToken, null);
        var existing = JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .FirstOrDefault(person =>
                person.GetProperty("firstName").GetString() == VolunteerFirstName &&
                person.GetProperty("lastName").GetString() == login);

        if (existing.ValueKind == JsonValueKind.Object)
        {
            return existing.GetProperty("id").GetString()!;
        }

        var body = JsonSerializer.Serialize(new
        {
            firstName = VolunteerFirstName,
            lastName = login,
            dateOfBirth = "1990-01-01T00:00:00Z",
            joined = "2024-01-01T00:00:00Z",
            isDriver = false,
            committed = false,
        });

        var created = await api.SendAsync(HttpMethod.Post, "/people", adminToken, body);
        var location = created.Headers.Location!;
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.ToString();
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];
    }
}

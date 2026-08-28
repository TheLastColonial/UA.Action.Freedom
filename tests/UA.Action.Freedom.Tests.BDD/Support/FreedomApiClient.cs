using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace UA.Action.Freedom.Tests.BDD.Support;

/// <summary>
/// Thin HTTP client for the deployed Freedom API plus password-grant token acquisition from
/// its OIDC provider. One instance per scenario (Reqnroll context injection).
/// </summary>
public sealed class FreedomApiClient : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly Dictionary<string, string> tokenCache = new();

    public HttpResponseMessage? LastResponse { get; private set; }

    public string LastBody { get; private set; } = string.Empty;

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? bearerToken, string? jsonBody)
    {
        using var request = new HttpRequestMessage(method, FreedomTarget.BaseUrl.TrimEnd('/') + path);

        if (!string.IsNullOrEmpty(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        LastResponse = await Http.SendAsync(request);
        LastBody = await LastResponse.Content.ReadAsStringAsync();
        return LastResponse;
    }

    public async Task<string> TokenForAsync(string username)
    {
        if (tokenCache.TryGetValue(username, out var cached))
        {
            return cached;
        }

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = FreedomTarget.OidcClientId,
            ["client_secret"] = FreedomTarget.OidcClientSecret,
            ["username"] = username,
            ["password"] = FreedomTarget.TestUserPassword,
            ["scope"] = "openid",
        });

        using var response = await Http.PostAsync(FreedomTarget.OidcTokenEndpoint, form);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Could not get a token for '{username}' from {FreedomTarget.OidcTokenEndpoint}: {(int)response.StatusCode} {body}");
        }

        var token = JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()!;
        tokenCache[username] = token;
        return token;
    }

    /// <summary>True when the deployed API answers a liveness probe and exposes /vehicles.</summary>
    public async Task<(bool Ok, string Reason)> ProbeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));

        try
        {
            var root = FreedomTarget.BaseUrl.TrimEnd('/');

            using var live = await Http.GetAsync($"{root}/health/live", timeout.Token);
            if (!live.IsSuccessStatusCode)
            {
                return (false, $"/health/live returned {(int)live.StatusCode}");
            }

            using var vehicles = await Http.GetAsync($"{root}/vehicles", timeout.Token);
            if (vehicles.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (false, "the deployed API has no /vehicles route (image predates this feature)");
            }

            return (true, string.Empty);
        }
        catch (Exception exception)
        {
            return (false, $"{FreedomTarget.BaseUrl} is not reachable: {exception.Message}");
        }
    }

    public void Dispose() => LastResponse?.Dispose();
}

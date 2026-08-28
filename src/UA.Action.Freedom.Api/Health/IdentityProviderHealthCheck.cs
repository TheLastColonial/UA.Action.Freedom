using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.Api.Configuration;

namespace UA.Action.Freedom.Api.Health;

/// <summary>
/// Fetches the identity provider's OpenID Connect discovery document.
/// </summary>
/// <remarks>
/// Deliberately checks discovery rather than a plain TCP connect. A running Keycloak with
/// no <c>freedom</c> realm — the state before <c>tofu apply</c> — answers on the port but
/// 404s on discovery, and it is the second case that stops anyone signing in.
/// </remarks>
public sealed class IdentityProviderHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptions<OidcOptions> options) : IHealthCheck
{
    public const string Name = "identity";

    private readonly OidcOptions _oidc = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var discovery = _oidc.DiscoveryEndpoint;

        if (string.IsNullOrWhiteSpace(discovery))
        {
            return HealthCheckResult.Unhealthy("No identity provider is configured.");
        }

        try
        {
            using var client = httpClientFactory.CreateClient(Name);
            using var response = await client.GetAsync(discovery, cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"Discovery document served from {discovery}.")
                : HealthCheckResult.Unhealthy(
                    $"Discovery at {discovery} answered {(int)response.StatusCode}. Has the realm been created?");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Could not reach the identity provider.", exception);
        }
    }
}

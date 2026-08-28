using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace UA.Action.Freedom.Api.Configuration;

/// <summary>
/// JWT bearer authentication against the OIDC provider named by <see cref="OidcOptions"/> —
/// Keycloak locally, Microsoft Entra External ID in Azure. Both put app roles in a flat
/// <c>roles</c> claim (keycloak.tf, recommendations §4.7), so the policies here port
/// unchanged. When nothing is configured the scheme still registers; protected endpoints
/// then answer 401 rather than the application failing to start.
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>Read any vehicle — every operational role.</summary>
    public const string VehiclesRead = "vehicles:read";

    /// <summary>Create, change or remove a vehicle — Purchaser and Administrator only.</summary>
    public const string VehiclesWrite = "vehicles:write";

    private const string RoleClaimType = "roles";

    private const string Administrator = "Administrator";
    private const string Purchaser = "Purchaser";
    private const string Dispatcher = "Dispatcher";
    private const string Loader = "Loader";

    public static IServiceCollection AddFreedomAuthentication(
        this IServiceCollection services, OidcOptions oidc, bool isDevelopment)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Production insists on HTTPS metadata; the local Keycloak and the in-memory
                // component tests are served over plain HTTP.
                options.RequireHttpsMetadata = oidc.RequireHttpsMetadata && !isDevelopment;

                // Keep claim names as the token sends them, so the flat `roles` claim stays
                // `roles` rather than being rewritten to the WS-Federation role URI.
                options.MapInboundClaims = false;

                if (!string.IsNullOrWhiteSpace(oidc.DiscoveryEndpoint))
                {
                    options.MetadataAddress = oidc.DiscoveryEndpoint;
                }
                else if (!string.IsNullOrWhiteSpace(oidc.Authority))
                {
                    options.Authority = oidc.Authority;
                }

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    RoleClaimType = RoleClaimType,
                    ValidateAudience = !string.IsNullOrWhiteSpace(oidc.Audience),
                    ValidAudience = oidc.Audience,
                };
            });

        return services;
    }

    public static IServiceCollection AddFreedomAuthorization(this IServiceCollection services)
    {
        services
            .AddAuthorizationBuilder()
            .AddPolicy(VehiclesRead, policy =>
                policy.RequireRole(Administrator, Purchaser, Dispatcher, Loader))
            .AddPolicy(VehiclesWrite, policy =>
                policy.RequireRole(Administrator, Purchaser));

        return services;
    }
}

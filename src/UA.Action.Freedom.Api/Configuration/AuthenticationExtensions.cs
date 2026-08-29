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

    /// <summary>Read the volunteer roster — every operational role.</summary>
    public const string PeopleRead = "people:read";

    /// <summary>
    /// Add, change or remove a volunteer — Administrator only. Approving new volunteers and
    /// revoking access when they leave is what the Administrator role exists for
    /// (docs/domain/key-concepts.md § Roles).
    /// </summary>
    public const string PeopleWrite = "people:write";

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

                // Authority is the browser-facing issuer the token's `iss` must match.
                // MetadataAddress, when given, is the URL this process fetches discovery and
                // signing keys from — a different host under split-horizon DNS (the local
                // Keycloak: issuer on localhost, backchannel on the compose network).
                options.Authority = oidc.Authority;

                if (!string.IsNullOrWhiteSpace(oidc.MetadataAddress))
                {
                    options.MetadataAddress = oidc.MetadataAddress;
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
                policy.RequireRole(Administrator, Purchaser))
            .AddPolicy(PeopleRead, policy =>
                policy.RequireRole(Administrator, Purchaser, Dispatcher, Loader))
            .AddPolicy(PeopleWrite, policy =>
                policy.RequireRole(Administrator));

        return services;
    }
}

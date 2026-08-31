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

    /// <summary>Read convoys, their route and their truck list — every operational role.</summary>
    public const string ConvoysRead = "convoys:read";

    /// <summary>
    /// Plan a convoy, set its route, and publish its truck list — Administrator and Dispatcher.
    /// Creating the manifest and coordinating the convoy is the Dispatcher's job
    /// (docs/domain/key-concepts.md § Roles).
    /// </summary>
    public const string ConvoysWrite = "convoys:write";

    /// <summary>
    /// Read receivers — reference, organisation and region only. Every operational role, plus
    /// the Ground Officer: this is the half that may appear on a document which crosses a border.
    /// </summary>
    public const string ReceiversRead = "receivers:read";

    /// <summary>Register or amend a receiving organisation — Administrator and Ground Officer.</summary>
    public const string ReceiversWrite = "receivers:write";

    /// <summary>
    /// Resolve, record or remove a Ukrainian delivery address — <strong>Ground Officer alone</strong>.
    /// </summary>
    /// <remarks>
    /// The narrowest policy in the API, and the reason the role exists: segregating delivery
    /// logistics from delivery detail (docs/domain/key-concepts.md § Ground Officer). Do not add
    /// a role here without reading recommendations §4.4 first. Note that the policy is only the
    /// outermost of three controls — the Ground Officer database identity and the DENY on the
    /// sensitive schema hold even if this list is widened by mistake.
    /// </remarks>
    public const string ReceiversDetail = "receivers:detail";

    /// <summary>Read boxes and their contents — every operational role.</summary>
    public const string BoxesRead = "boxes:read";

    /// <summary>Pack, move or remove a box — Administrator, Dispatcher and Loader.</summary>
    public const string BoxesWrite = "boxes:write";

    /// <summary>
    /// Confirm a box's contents and weight — Administrator and Loader.
    /// </summary>
    /// <remarks>
    /// The Loader is the role that stands in the warehouse and opens the box, so this is theirs.
    /// It is separate from <see cref="BoxesWrite"/> because packing a box and vouching for what
    /// is in it are different acts: the validation record is what the charity's assurance to a
    /// border rests on (docs/domain/key-concepts.md § Loader).
    /// </remarks>
    public const string BoxesValidate = "boxes:validate";

    /// <summary>Read manifests, their teams, their cargo and their weight — every operational role.</summary>
    public const string ManifestsRead = "manifests:read";

    /// <summary>
    /// Build a manifest and move it through its lifecycle — Administrator and Dispatcher.
    /// Creating the manifest is what the Dispatcher role exists for.
    /// </summary>
    public const string ManifestsWrite = "manifests:write";

    /// <summary>
    /// Approve a manifest — Administrator only.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ManifestsWrite"/> because approval is not another edit. It
    /// releases the Goods Movement Reference to HMRC and freezes the manifest for good
    /// (docs/process.puml, recommendations §5.2), so the person who builds a manifest is not
    /// the person who signs it off.
    /// </remarks>
    public const string ManifestsApprove = "manifests:approve";

    private const string RoleClaimType = "roles";

    private const string Administrator = "Administrator";
    private const string Purchaser = "Purchaser";
    private const string Dispatcher = "Dispatcher";
    private const string Loader = "Loader";

    /// <summary>
    /// The only role that sees full receiver detail. Deliberately absent from every other
    /// policy: a Ground Officer has no reason to read the vehicle roster or the volunteer list,
    /// and the isolation runs both ways.
    /// </summary>
    private const string GroundOfficer = "GroundOfficer";

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
                policy.RequireRole(Administrator))
            .AddPolicy(ConvoysRead, policy =>
                policy.RequireRole(Administrator, Purchaser, Dispatcher, Loader))
            .AddPolicy(ConvoysWrite, policy =>
                policy.RequireRole(Administrator, Dispatcher))
            .AddPolicy(ReceiversRead, policy =>
                policy.RequireRole(Administrator, Purchaser, Dispatcher, Loader, GroundOfficer))
            .AddPolicy(ReceiversWrite, policy =>
                policy.RequireRole(Administrator, GroundOfficer))
            .AddPolicy(ReceiversDetail, policy =>
                policy.RequireRole(GroundOfficer))
            .AddPolicy(BoxesRead, policy =>
                policy.RequireRole(Administrator, Purchaser, Dispatcher, Loader))
            .AddPolicy(BoxesWrite, policy =>
                policy.RequireRole(Administrator, Dispatcher, Loader))
            .AddPolicy(BoxesValidate, policy =>
                policy.RequireRole(Administrator, Loader))
            .AddPolicy(ManifestsRead, policy =>
                policy.RequireRole(Administrator, Purchaser, Dispatcher, Loader))
            .AddPolicy(ManifestsWrite, policy =>
                policy.RequireRole(Administrator, Dispatcher))
            .AddPolicy(ManifestsApprove, policy =>
                policy.RequireRole(Administrator));

        return services;
    }
}

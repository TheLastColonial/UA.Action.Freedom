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

    /// <summary>Create, change or remove a vehicle — Administrator, Purchaser and Mechanic.</summary>
    public const string VehiclesWrite = "vehicles:write";

    /// <summary>
    /// Record a vehicle's servicing inspection — Administrator and Mechanic.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="VehiclesWrite"/> because the result is what decides whether a
    /// vehicle may join a convoy: a Purchaser who can edit a vehicle's details cannot also pass
    /// it as roadworthy (docs/domain/key-concepts.md § Mechanic).
    /// </remarks>
    public const string VehiclesService = "vehicles:service";

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
    /// Record whether a receiver is registered, suspended or expired — <strong>Administrator only</strong>,
    /// narrower than <see cref="ReceiversWrite"/> because registration is an act of authorisation and the
    /// Ground Officer, who writes the receiver, must not grant it (ADR 0012). Also reads a receiver's usage.
    /// </summary>
    public const string ReceiversRegister = "receivers:register";

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

    /// <summary>
    /// Place or move a box within a bay — <strong>Loader only</strong>.
    /// </summary>
    /// <remarks>
    /// Narrower even than <see cref="BoxesValidate"/>: this is the on-site, physical act of
    /// shelving a box, not a coordination task, so it excludes Administrator and Dispatcher as
    /// well (docs/domain/key-concepts.md § Loader).
    /// </remarks>
    public const string BoxesAllocateBay = "boxes:allocate-bay";

    /// <summary>Read donors, their donations and the donor status report — every operational role.</summary>
    public const string DonationsRead = "donations:read";

    /// <summary>
    /// Enter or correct a donor or a donation — Administrator, Dispatcher and Loader (O22): the people who take a
    /// donation in. Not Purchaser or Mechanic, and never GroundOfficer.
    /// </summary>
    public const string DonationsWrite = "donations:write";

    /// <summary>
    /// Erase a donor — Administrator only, as for volunteers. Erasure deletes personal data for good, so it is not
    /// part of entering one.
    /// </summary>
    public const string DonorsErase = "donors:erase";

    /// <summary>Read the item categories and the customs code each maps to — every operational role.</summary>
    public const string CategoriesRead = "categories:read";

    /// <summary>
    /// Create or change a category, or map it to a customs code — Administrator only (O31). The mapping decides what
    /// is declared at a border, so the people who pack boxes do not set it.
    /// </summary>
    public const string CategoriesWrite = "categories:write";

    /// <summary>Read distribution hubs and their bays — every operational role.</summary>
    public const string LocationsRead = "locations:read";

    /// <summary>
    /// Create, change or remove a location or its bays — Administrator only. Setting up a depot
    /// is infrastructure, not day-to-day box handling.
    /// </summary>
    public const string LocationsWrite = "locations:write";

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

    /// <summary>
    /// Record or withdraw the ICS2 Entry Summary Declaration a manifest's crossing was accepted
    /// under — Administrator and Dispatcher.
    /// </summary>
    /// <remarks>
    /// Its own policy rather than part of <see cref="ManifestsWrite"/>, because it is not an edit to
    /// the manifest: it is the one fact approval will not proceed without, and withdrawing it strands
    /// a convoy as surely as deleting a vehicle would.
    ///
    /// <para>
    /// Deliberately <strong>not</strong> GroundOfficer, even though a Ground Officer is who files the
    /// declaration — filing it needs the Ukrainian consignee address, which only that role may read.
    /// GroundOfficer is excluded from every other policy and the isolation runs both ways
    /// (<c>docs/local-authentication.md</c>), and that is worth more than saving a hand-off. So the
    /// filer obtains the MRN in the EU Customs Trader Portal and a Dispatcher records it here, the
    /// same two-person shape as a Dispatcher building a manifest an Administrator signs off.
    /// </para>
    /// </remarks>
    public const string ManifestsDeclare = "manifests:declare";

    /// <summary>
    /// Assign or unassign a driver to/from a vehicle on a convoy — Dispatcher only.
    /// </summary>
    /// <remarks>
    /// Narrower than <see cref="ConvoysWrite"/> (which also allows Administrator): crewing
    /// vehicles is day-to-day convoy coordination, which is what the Dispatcher role exists for
    /// (docs/domain/key-concepts.md § Roles). Every operational role can still read the crew
    /// list via <see cref="ConvoysRead"/>.
    /// </remarks>
    public const string ConvoysAssignDrivers = "convoys:assign-drivers";

    private const string RoleClaimType = "roles";

    private const string Administrator = "Administrator";
    private const string Purchaser = "Purchaser";
    private const string Dispatcher = "Dispatcher";
    private const string Loader = "Loader";

    /// <summary>
    /// Vehicles only: reads and edits the fleet and records servicing inspections. Absent from
    /// every other policy — a Mechanic has no reason to see convoys, cargo or volunteers.
    /// </summary>
    private const string Mechanic = "Mechanic";

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
                policy.RequireRole(Administrator, Purchaser, Dispatcher, Loader, Mechanic))
            .AddPolicy(VehiclesWrite, policy =>
                policy.RequireRole(Administrator, Purchaser, Mechanic))
            .AddPolicy(VehiclesService, policy =>
                policy.RequireRole(Administrator, Mechanic))
            .AddPolicy(PeopleRead, policy =>
                policy.RequireRole(Administrator, Purchaser, Dispatcher, Loader))
            .AddPolicy(PeopleWrite, policy =>
                policy.RequireRole(Administrator))
            .AddPolicy(ConvoysRead, policy =>
                policy.RequireRole(Administrator, Purchaser, Dispatcher, Loader))
            .AddPolicy(ConvoysWrite, policy =>
                policy.RequireRole(Administrator, Dispatcher))
            .AddPolicy(ConvoysAssignDrivers, policy =>
                policy.RequireRole(Dispatcher))
            .AddPolicy(ReceiversRead, policy =>
                policy.RequireRole(Administrator, Purchaser, Dispatcher, Loader, GroundOfficer))
            .AddPolicy(ReceiversWrite, policy =>
                policy.RequireRole(Administrator, GroundOfficer))
            .AddPolicy(ReceiversRegister, policy =>
                policy.RequireRole(Administrator))
            .AddPolicy(ReceiversDetail, policy =>
                policy.RequireRole(GroundOfficer))
            .AddPolicy(BoxesRead, policy =>
                policy.RequireRole(Administrator, Purchaser, Dispatcher, Loader))
            .AddPolicy(BoxesWrite, policy =>
                policy.RequireRole(Administrator, Dispatcher, Loader))
            .AddPolicy(BoxesValidate, policy =>
                policy.RequireRole(Administrator, Loader))
            .AddPolicy(BoxesAllocateBay, policy =>
                policy.RequireRole(Loader))
            .AddPolicy(DonationsRead, policy =>
                policy.RequireRole(Administrator, Purchaser, Dispatcher, Loader))
            .AddPolicy(DonationsWrite, policy =>
                policy.RequireRole(Administrator, Dispatcher, Loader))
            .AddPolicy(DonorsErase, policy =>
                policy.RequireRole(Administrator))
            .AddPolicy(CategoriesRead, policy =>
                policy.RequireRole(Administrator, Purchaser, Dispatcher, Loader))
            .AddPolicy(CategoriesWrite, policy =>
                policy.RequireRole(Administrator))
            .AddPolicy(LocationsRead, policy =>
                policy.RequireRole(Administrator, Purchaser, Dispatcher, Loader))
            .AddPolicy(LocationsWrite, policy =>
                policy.RequireRole(Administrator))
            .AddPolicy(ManifestsRead, policy =>
                policy.RequireRole(Administrator, Purchaser, Dispatcher, Loader))
            .AddPolicy(ManifestsWrite, policy =>
                policy.RequireRole(Administrator, Dispatcher))
            .AddPolicy(ManifestsApprove, policy =>
                policy.RequireRole(Administrator))
            .AddPolicy(ManifestsDeclare, policy =>
                policy.RequireRole(Administrator, Dispatcher));

        return services;
    }
}

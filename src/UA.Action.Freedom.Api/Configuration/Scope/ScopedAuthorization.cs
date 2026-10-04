using System.Diagnostics.Metrics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Api.Configuration.Scope;

/// <summary>The thing a scoped decision is about: one convoy, or one location (or none yet).</summary>
public interface IScopedResource;

/// <summary>A convoy, by its identifier. Reached by a Convoy Leader only while they lead it.</summary>
public sealed record ConvoyScope(int ConvoyId) : IScopedResource;

/// <summary>
/// A location, or no location (<c>null</c>: a box that is expected but not yet at a hub). A scoped Loader reaches an
/// unlocated box only where <paramref name="AllowUnlocated"/> says so, which is a read and the move that checks it in.
/// </summary>
public sealed record LocationScope(int? LocationId, bool AllowUnlocated) : IScopedResource;

public enum ScopeKind
{
    Convoy,
    Location
}

/// <summary>
/// A route needs this on top of its role policy. <paramref name="ExemptRoles"/> are the roles the route already gave
/// whole-system reach, so scope does not narrow them.
/// </summary>
public sealed record ScopedRequirement(ScopeKind Kind, IReadOnlyList<string> ExemptRoles) : IAuthorizationRequirement;

public static class ScopedRequirements
{
    public const string Administrator = "Administrator";
    public const string Purchaser = "Purchaser";
    public const string Dispatcher = "Dispatcher";
    public const string Loader = "Loader";
    public const string ConvoyLeader = "ConvoyLeader";
    public const string GroundOfficer = "GroundOfficer";
    public const string RoleClaimType = "roles";

    /// <summary>Convoy routes: only the derived <c>ConvoyLeader</c> is narrowed to the convoy it leads.</summary>
    public static ScopedRequirement Convoy { get; } =
        new(ScopeKind.Convoy, [Administrator, Purchaser, Dispatcher, Loader]);

    /// <summary>Box and location routes: only a <c>Loader</c> is narrowed to the locations they manage (O14).</summary>
    public static ScopedRequirement Location { get; } =
        new(ScopeKind.Location, [Administrator, Purchaser, Dispatcher]);
}

internal static class ScopeMetrics
{
    private static readonly Meter Meter = new("UA.Action.Freedom.Authorization");

    private static readonly Counter<long> Decisions = Meter.CreateCounter<long>(
        "freedom.authz.scope", description: "Resource-scoped authorization decisions, by kind, outcome and reason.");

    public static void Record(ScopeKind kind, bool allowed, string reason) =>
        Decisions.Add(
            1,
            new KeyValuePair<string, object?>("kind", kind == ScopeKind.Convoy ? "convoy" : "location"),
            new KeyValuePair<string, object?>("outcome", allowed ? "allowed" : "denied"),
            new KeyValuePair<string, object?>("reason", reason));
}

/// <summary>
/// The one handler behind every resource-scoped route (ADR 0010). It never fails explicitly and never succeeds by
/// default: it calls <c>Succeed</c> only when a rule below holds, so anything it cannot place is a denial. Assignments
/// are read from the store on every call, and a store failure propagates rather than reading as success.
/// </summary>
public sealed class ScopedAuthorizationHandler(ICurrentPerson currentPerson, IScopeAssignments assignments)
    : AuthorizationHandler<ScopedRequirement, IScopedResource>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ScopedRequirement requirement, IScopedResource resource)
    {
        var user = context.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            ScopeMetrics.Record(requirement.Kind, false, "not-authenticated");
            return;
        }

        if (requirement.ExemptRoles.Any(role => user.HasClaim(ScopedRequirements.RoleClaimType, role)))
        {
            ScopeMetrics.Record(requirement.Kind, true, "exempt-role");
            context.Succeed(requirement);
            return;
        }

        if (await currentPerson.ResolveAsync(CancellationToken.None) is not CurrentPerson.Linked(var personId))
        {
            ScopeMetrics.Record(requirement.Kind, false, "not-linked");
            return;
        }

        var allowed = (requirement.Kind, resource) switch
        {
            (ScopeKind.Convoy, ConvoyScope convoy) =>
                user.HasClaim(ScopedRequirements.RoleClaimType, ScopedRequirements.ConvoyLeader)
                && await assignments.IsCurrentLeaderAsync(convoy.ConvoyId, personId, CancellationToken.None),
            (ScopeKind.Location, LocationScope location) =>
                user.HasClaim(ScopedRequirements.RoleClaimType, ScopedRequirements.Loader)
                && await LoaderMayReachAsync(location, personId),
            _ => false,
        };

        ScopeMetrics.Record(requirement.Kind, allowed, allowed ? "assigned" : "no-assignment");

        if (allowed)
        {
            context.Succeed(requirement);
        }
    }

    private async Task<bool> LoaderMayReachAsync(LocationScope location, Guid personId) =>
        location.LocationId is int id
            ? await assignments.ManagesLocationAsync(personId, id, CancellationToken.None)
            : location.AllowUnlocated;
}

/// <summary>
/// <c>ConvoyLeader</c> is derived, not issued: a linked login with an open <c>ConvoyLeaderAssignment</c> holds it for
/// the request. The Dispatcher or Administrator who nominates the leader is the one who marks them, and no identity
/// provider is involved. A role of that name in a token is removed first, so only an assignment can grant it, and a
/// Ground Officer never receives it because the isolation of that role runs both ways.
/// </summary>
public sealed class LeaderRoleClaims(IPersonRepository people, IScopeAssignments assignments) : IClaimsTransformation
{
    private bool? leadsAConvoy;

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return principal;
        }

        var identities = principal.Identities.Select(StripLeaderRole).ToList();

        // The transformation runs inside authentication, before HttpContext.User is set, so ICurrentPerson (which reads
        // it and remembers its answer) must not be asked here: the subject is taken from the principal in hand.
        if (!principal.HasClaim(ScopedRequirements.RoleClaimType, ScopedRequirements.GroundOfficer)
            && await LeadsAConvoyAsync(principal))
        {
            identities.Add(new ClaimsIdentity(
                [new Claim(ScopedRequirements.RoleClaimType, ScopedRequirements.ConvoyLeader)],
                "FreedomLeaderAssignment", ClaimTypes.NameIdentifier, ScopedRequirements.RoleClaimType));
        }

        return new ClaimsPrincipal(identities);
    }

    private async Task<bool> LeadsAConvoyAsync(ClaimsPrincipal principal)
    {
        if (leadsAConvoy is { } known)
        {
            return known;
        }

        var subject = ClaimsCurrentPerson.SubjectOf(principal);
        if (string.IsNullOrWhiteSpace(subject)
            || await people.FindBySubjectAsync(subject, CancellationToken.None) is not Guid personId)
        {
            return (leadsAConvoy = false).Value;
        }

        return (leadsAConvoy = (await assignments.LedConvoyIdsAsync(personId, CancellationToken.None)).Count > 0).Value;
    }

    private static ClaimsIdentity StripLeaderRole(ClaimsIdentity identity)
    {
        var clone = identity.Clone();
        foreach (var claim in clone.FindAll(c =>
                     c.Type == ScopedRequirements.RoleClaimType && c.Value == ScopedRequirements.ConvoyLeader).ToList())
        {
            clone.RemoveClaim(claim);
        }

        return clone;
    }
}

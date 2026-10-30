using Microsoft.AspNetCore.Authorization;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Boxes;

namespace UA.Action.Freedom.Api.Configuration.Scope;

/// <summary>
/// The caller's reach, asked of the one <see cref="ScopedAuthorizationHandler"/> (ADR 0010). Endpoint filters and the few
/// body-dependent checks go through this, so there is a single place a scope decision is made.
/// </summary>
public interface IScopeGuard
{
    /// <summary>True when the caller holds none of the roles that see every location, so scope narrows them.</summary>
    bool LocationScopeApplies { get; }

    Task<bool> CanAccessConvoyAsync(int convoyId, CancellationToken cancellationToken);

    Task<bool> CanAccessLocationAsync(int? locationId, bool allowUnlocated, CancellationToken cancellationToken);

    /// <summary>What the caller may see in a list. Everything for an exempt role; otherwise only what is assigned.</summary>
    Task<LocationVisibility> LocationVisibilityAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The 403 to return after a refusal. A login nobody has linked to a volunteer is told so (it cannot be scoped to
    /// anything until it is), and anyone else is told the route is out of their reach.
    /// </summary>
    Task<IResult> RefusalAsync(CancellationToken cancellationToken);
}

public sealed class ScopeGuard(
    IHttpContextAccessor accessor,
    IAuthorizationService authorization,
    ICurrentPerson currentPerson,
    IScopeAssignments assignments) : IScopeGuard
{
    private System.Security.Claims.ClaimsPrincipal User =>
        accessor.HttpContext?.User ?? new System.Security.Claims.ClaimsPrincipal();

    public bool LocationScopeApplies =>
        !ScopedRequirements.Location.ExemptRoles.Any(role => User.HasClaim(ScopedRequirements.RoleClaimType, role));

    public async Task<bool> CanAccessConvoyAsync(int convoyId, CancellationToken cancellationToken) =>
        (await authorization.AuthorizeAsync(User, new ConvoyScope(convoyId), ScopedRequirements.Convoy)).Succeeded;

    public async Task<bool> CanAccessLocationAsync(int? locationId, bool allowUnlocated, CancellationToken cancellationToken) =>
        (await authorization.AuthorizeAsync(
            User, new LocationScope(locationId, allowUnlocated), ScopedRequirements.Location)).Succeeded;

    public async Task<IResult> RefusalAsync(CancellationToken cancellationToken) =>
        await currentPerson.ResolveAsync(cancellationToken) is CurrentPerson.Linked
            ? ScopeProblems.Forbidden()
            : LoginNotLinked.Problem();

    public async Task<LocationVisibility> LocationVisibilityAsync(CancellationToken cancellationToken)
    {
        if (!LocationScopeApplies)
        {
            return LocationVisibility.All;
        }

        if (User.HasClaim(ScopedRequirements.RoleClaimType, ScopedRequirements.Loader)
            && await currentPerson.ResolveAsync(cancellationToken) is CurrentPerson.Linked(var personId))
        {
            return LocationVisibility.Only(
                await assignments.ManagedLocationIdsAsync(personId, cancellationToken), includeUnlocated: true);
        }

        return LocationVisibility.Only([], includeUnlocated: false);
    }
}

public enum BoxAccess
{
    /// <summary>The box at an assigned location, or unlocated.</summary>
    Read,

    /// <summary>The box at an assigned location. An unlocated box is not writable.</summary>
    Write,

    /// <summary>As <see cref="Read"/> for the source, so an unlocated box can be checked in; the destination is checked by the route.</summary>
    Move
}

/// <summary>Marks an endpoint as resource-scoped, for the endpoint-map test.</summary>
public sealed record ScopedEndpointMetadata(ScopeKind Kind);

/// <summary>Marks an endpoint as deliberately not resource-scoped, with the reason, for the endpoint-map test.</summary>
public sealed record ScopeExemptMetadata(string Reason);

public static class ScopeProblems
{
    public const string OutOfScope = "out-of-scope";

    public static IResult Forbidden() => Results.Problem(
        type: OutOfScope,
        title: "This is outside what you manage.",
        detail: "Your role reaches only the convoy you lead or the locations you manage.",
        statusCode: StatusCodes.Status403Forbidden);
}

internal sealed class ConvoyScopeFilter(string routeKey) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var guard = http.RequestServices.GetRequiredService<IScopeGuard>();
        if (!int.TryParse(http.Request.RouteValues[routeKey]?.ToString(), out var convoyId)
            || !await guard.CanAccessConvoyAsync(convoyId, http.RequestAborted))
        {
            return await guard.RefusalAsync(http.RequestAborted);
        }

        return await next(context);
    }
}

internal sealed class LocationScopeFilter(string routeKey) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var guard = http.RequestServices.GetRequiredService<IScopeGuard>();
        if (!int.TryParse(http.Request.RouteValues[routeKey]?.ToString(), out var locationId)
            || !await guard.CanAccessLocationAsync(locationId, allowUnlocated: false, http.RequestAborted))
        {
            return await guard.RefusalAsync(http.RequestAborted);
        }

        return await next(context);
    }
}

internal sealed class BoxScopeFilter(BoxAccess access, string routeKey) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var guard = http.RequestServices.GetRequiredService<IScopeGuard>();

        if (!guard.LocationScopeApplies)
        {
            return await next(context);
        }

        if (!int.TryParse(http.Request.RouteValues[routeKey]?.ToString(), out var boxId))
        {
            return await guard.RefusalAsync(http.RequestAborted);
        }

        var box = await http.RequestServices.GetRequiredService<IBoxRepository>().GetByIdAsync(boxId, http.RequestAborted);
        if (box is null)
        {
            return await next(context);
        }

        return await guard.CanAccessLocationAsync(box.LocationId, access != BoxAccess.Write, http.RequestAborted)
            ? await next(context)
            : await guard.RefusalAsync(http.RequestAborted);
    }
}

public static class ScopeEndpointExtensions
{
    /// <summary>The caller must lead this convoy (or hold a role that reaches every convoy). The convoy id is a route value.</summary>
    public static TBuilder RequireConvoyScope<TBuilder>(this TBuilder builder, string routeKey = "id")
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new ScopedEndpointMetadata(ScopeKind.Convoy)).AddEndpointFilter(new ConvoyScopeFilter(routeKey));

    /// <summary>The caller must manage this location (or hold a role that reaches every location).</summary>
    public static TBuilder RequireLocationScope<TBuilder>(this TBuilder builder, string routeKey = "id")
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new ScopedEndpointMetadata(ScopeKind.Location)).AddEndpointFilter(new LocationScopeFilter(routeKey));

    /// <summary>The caller must reach the box, by where it is. The box id is a route value.</summary>
    public static TBuilder RequireBoxScope<TBuilder>(this TBuilder builder, BoxAccess access, string routeKey = "id")
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new ScopedEndpointMetadata(ScopeKind.Location)).AddEndpointFilter(new BoxScopeFilter(access, routeKey));

    /// <summary>
    /// The route is reachable by a scoped role but is deliberately not narrowed, or is narrowed by its own body check.
    /// The reason is pinned by the endpoint-map test.
    /// </summary>
    public static TBuilder ScopeExempt<TBuilder>(this TBuilder builder, string reason)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new ScopeExemptMetadata(reason));
}

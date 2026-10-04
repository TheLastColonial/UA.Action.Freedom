using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using UA.Action.Freedom.Api.Configuration.Scope;

namespace UA.Action.Freedom.Tests.Component.Scope;

/// <summary>
/// The build fails for a forgotten route (ADR 0010): every endpoint a scoped role can reach under a box, location or
/// led-convoy policy must say how it is scoped, or say why it is not, and the exemptions are pinned.
/// </summary>
public class ScopeEndpointMapTests
{
    private static readonly string[] ScopedRoles = ["Loader", "ConvoyLeader"];

    private static bool IsScopedPolicy(string policy) =>
        policy.StartsWith("boxes:", StringComparison.Ordinal)
        || policy.StartsWith("locations:", StringComparison.Ordinal)
        || policy == "convoys:read-led";

    private record Described(string Name, string Policy, bool Scoped, string? ExemptReason);

    private static async Task<IReadOnlyList<Described>> ReachableByAScopedRoleAsync()
    {
        await using var api = FreedomApi.WithScope(roles: "Administrator");
        var endpoints = api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        var policies = api.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var found = new List<Described>();

        foreach (var endpoint in endpoints)
        {
            var policyName = endpoint.Metadata.GetMetadata<IAuthorizeData>()?.Policy;
            if (policyName is null || !IsScopedPolicy(policyName))
            {
                continue;
            }

            var policy = await policies.GetPolicyAsync(policyName);
            var roles = policy!.Requirements.OfType<RolesAuthorizationRequirement>().SelectMany(r => r.AllowedRoles);
            if (!roles.Intersect(ScopedRoles).Any())
            {
                continue;
            }

            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            found.Add(new Described(
                $"{string.Join('|', methods)} {endpoint.RoutePattern.RawText}".TrimEnd('/'),
                policyName,
                endpoint.Metadata.GetMetadata<ScopedEndpointMetadata>() is not null,
                endpoint.Metadata.GetMetadata<ScopeExemptMetadata>()?.Reason));
        }

        return found;
    }

    [Fact]
    public async Task Every_route_a_scoped_role_can_reach_declares_its_scope_or_its_exemption()
    {
        var reachable = await ReachableByAScopedRoleAsync();

        reachable.Should().NotBeEmpty("the walk must actually find the box and location routes");
        reachable.Where(route => !route.Scoped && route.ExemptReason is null)
            .Select(route => $"{route.Name} ({route.Policy})")
            .Should().BeEmpty("a route a Loader or Convoy Leader can reach must be scoped (RequireBoxScope, RequireLocationScope, RequireConvoyScope) or marked ScopeExempt with a reason");
    }

    [Fact]
    public async Task The_exempt_routes_are_pinned()
    {
        var reachable = await ReachableByAScopedRoleAsync();

        reachable.Where(route => route.ExemptReason is not null).Select(route => route.Name).Should().BeEquivalentTo(
            "POST /boxes",
            "GET /boxes/scan/{token:guid}",
            "GET /boxes",
            "GET /locations");
    }
}

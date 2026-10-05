using System.Security.Claims;
using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Api.Configuration.Scope;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Component.Scope;

/// <summary>
/// ConvoyLeader is derived from an open assignment, not issued by the identity provider (ADR 0010 section 1).
/// </summary>
public class LeaderRoleClaimsTests
{
    private static readonly Guid Person = new("7e57a5e2-0000-4000-8000-000000000001");

    private static ClaimsPrincipal ACaller(params string[] roles) =>
        new(new ClaimsIdentity(
            roles.Select(role => new Claim("roles", role)).Append(new Claim("sub", "kc-1")),
            "Test", ClaimTypes.NameIdentifier, "roles"));

    private static LeaderRoleClaims Transformation(bool linked, params int[] led)
    {
        var people = Substitute.For<IPersonRepository>();
        people.FindBySubjectAsync("kc-1", Arg.Any<CancellationToken>()).Returns(linked ? Person : null);
        var assignments = Substitute.For<IScopeAssignments>();
        assignments.LedConvoyIdsAsync(Person, Arg.Any<CancellationToken>()).Returns(led);
        return new LeaderRoleClaims(people, assignments);
    }

    [Fact]
    public async Task A_linked_login_with_an_open_assignment_is_given_the_role()
    {
        var principal = await Transformation(true, 12).TransformAsync(ACaller("Loader"));

        principal.HasClaim("roles", "ConvoyLeader").Should().BeTrue();
        principal.HasClaim("roles", "Loader").Should().BeTrue();
    }

    [Fact]
    public async Task A_linked_login_with_no_assignment_is_not()
    {
        (await Transformation(true).TransformAsync(ACaller())).HasClaim("roles", "ConvoyLeader").Should().BeFalse();
    }

    [Fact]
    public async Task An_unlinked_login_is_not()
    {
        (await Transformation(false, 12).TransformAsync(ACaller())).HasClaim("roles", "ConvoyLeader")
            .Should().BeFalse();
    }

    [Fact]
    public async Task A_ground_officer_is_never_given_it()
    {
        (await Transformation(true, 12).TransformAsync(ACaller("GroundOfficer"))).HasClaim("roles", "ConvoyLeader")
            .Should().BeFalse();
    }

    [Fact]
    public async Task A_token_that_claims_the_role_is_not_trusted_without_an_assignment()
    {
        var principal = await Transformation(true).TransformAsync(ACaller("ConvoyLeader"));

        principal.HasClaim("roles", "ConvoyLeader").Should().BeFalse();
    }

    [Fact]
    public async Task Running_twice_adds_the_role_once()
    {
        var transformation = Transformation(true, 12);
        var once = await transformation.TransformAsync(ACaller());
        var twice = await transformation.TransformAsync(once);

        twice.FindAll("roles").Count(claim => claim.Value == "ConvoyLeader").Should().Be(1);
    }
}

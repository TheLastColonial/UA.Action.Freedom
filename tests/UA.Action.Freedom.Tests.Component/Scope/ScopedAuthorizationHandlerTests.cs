using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;
using UA.Action.Freedom.Api.Configuration.Scope;
using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Tests.Component.Scope;

/// <summary>
/// The one handler behind every resource-scoped route (ADR 0010): it succeeds only when a rule says so, and
/// everything it cannot place is a denial.
/// </summary>
public class ScopedAuthorizationHandlerTests
{
    private static readonly Guid Person = new("7e57a5e2-0000-4000-8000-000000000001");

    private const int ConvoyId = 12;
    private const int LocationId = 3;

    private static ClaimsPrincipal ACaller(params string[] roles) =>
        new(new ClaimsIdentity(
            roles.Select(role => new Claim("roles", role)), "Test", ClaimTypes.NameIdentifier, "roles"));

    private static ICurrentPerson Linked(bool linked = true)
    {
        var current = Substitute.For<ICurrentPerson>();
        current.ResolveAsync(Arg.Any<CancellationToken>())
            .Returns(linked ? new CurrentPerson.Linked(Person) : new CurrentPerson.NotLinked());
        return current;
    }

    private static async Task<bool> AllowedAsync(
        ClaimsPrincipal caller, IScopedResource resource, ScopedRequirement requirement,
        ICurrentPerson? currentPerson = null, IScopeAssignments? assignments = null)
    {
        var handler = new ScopedAuthorizationHandler(
            currentPerson ?? Linked(), assignments ?? Substitute.For<IScopeAssignments>());
        var context = new AuthorizationHandlerContext([requirement], caller, resource);

        await handler.HandleAsync(context);

        return context.HasSucceeded;
    }

    private static IScopeAssignments Leading(int convoyId)
    {
        var assignments = Substitute.For<IScopeAssignments>();
        assignments.IsCurrentLeaderAsync(convoyId, Person, Arg.Any<CancellationToken>()).Returns(true);
        return assignments;
    }

    private static IScopeAssignments Managing(int locationId)
    {
        var assignments = Substitute.For<IScopeAssignments>();
        assignments.ManagesLocationAsync(Person, locationId, Arg.Any<CancellationToken>()).Returns(true);
        return assignments;
    }

    [Fact]
    public async Task A_leader_with_a_current_assignment_may_act_on_that_convoy()
    {
        (await AllowedAsync(ACaller("ConvoyLeader"), new ConvoyScope(ConvoyId), ScopedRequirements.Convoy,
            assignments: Leading(ConvoyId))).Should().BeTrue();
    }

    [Fact]
    public async Task The_role_without_an_assignment_is_denied()
    {
        (await AllowedAsync(ACaller("ConvoyLeader"), new ConvoyScope(ConvoyId), ScopedRequirements.Convoy))
            .Should().BeFalse();
    }

    [Fact]
    public async Task An_assignment_to_another_convoy_is_denied()
    {
        (await AllowedAsync(ACaller("ConvoyLeader"), new ConvoyScope(ConvoyId + 1), ScopedRequirements.Convoy,
            assignments: Leading(ConvoyId))).Should().BeFalse();
    }

    [Fact]
    public async Task A_closed_assignment_is_denied()
    {
        var assignments = Substitute.For<IScopeAssignments>();
        assignments.IsCurrentLeaderAsync(ConvoyId, Person, Arg.Any<CancellationToken>()).Returns(false);

        (await AllowedAsync(ACaller("ConvoyLeader"), new ConvoyScope(ConvoyId), ScopedRequirements.Convoy,
            assignments: assignments)).Should().BeFalse();
    }

    [Fact]
    public async Task An_unlinked_login_is_denied_even_with_the_role()
    {
        (await AllowedAsync(ACaller("ConvoyLeader"), new ConvoyScope(ConvoyId), ScopedRequirements.Convoy,
            Linked(false), Leading(ConvoyId))).Should().BeFalse();
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_denied()
    {
        (await AllowedAsync(new ClaimsPrincipal(new ClaimsIdentity()), new ConvoyScope(ConvoyId),
            ScopedRequirements.Convoy, assignments: Leading(ConvoyId))).Should().BeFalse();
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Dispatcher")]
    public async Task An_exempt_role_is_not_scoped_and_needs_no_link(string role)
    {
        (await AllowedAsync(ACaller(role), new ConvoyScope(ConvoyId), ScopedRequirements.Convoy,
            Linked(false))).Should().BeTrue();
    }

    [Fact]
    public async Task A_loader_at_an_assigned_location_is_allowed()
    {
        (await AllowedAsync(ACaller("Loader"), new LocationScope(LocationId, AllowUnlocated: false),
            ScopedRequirements.Location, assignments: Managing(LocationId))).Should().BeTrue();
    }

    [Fact]
    public async Task A_loader_at_another_location_is_denied()
    {
        (await AllowedAsync(ACaller("Loader"), new LocationScope(LocationId + 1, AllowUnlocated: false),
            ScopedRequirements.Location, assignments: Managing(LocationId))).Should().BeFalse();
    }

    [Fact]
    public async Task A_loader_with_no_assignment_is_denied_every_located_box()
    {
        (await AllowedAsync(ACaller("Loader"), new LocationScope(LocationId, AllowUnlocated: true),
            ScopedRequirements.Location)).Should().BeFalse();
    }

    [Fact]
    public async Task An_unlocated_box_is_allowed_only_where_the_resource_says_so()
    {
        var loader = ACaller("Loader");

        (await AllowedAsync(loader, new LocationScope(null, AllowUnlocated: true), ScopedRequirements.Location))
            .Should().BeTrue();
        (await AllowedAsync(loader, new LocationScope(null, AllowUnlocated: false), ScopedRequirements.Location))
            .Should().BeFalse();
    }

    [Fact]
    public async Task A_loader_who_is_not_linked_is_denied_at_their_own_location()
    {
        (await AllowedAsync(ACaller("Loader"), new LocationScope(LocationId, AllowUnlocated: false),
            ScopedRequirements.Location, Linked(false), Managing(LocationId))).Should().BeFalse();
    }

    [Fact]
    public async Task One_scoped_role_does_not_rescue_another_kind_of_scope()
    {
        (await AllowedAsync(ACaller("ConvoyLeader"), new LocationScope(LocationId, AllowUnlocated: false),
            ScopedRequirements.Location, assignments: Managing(LocationId))).Should().BeFalse();
        (await AllowedAsync(ACaller("Mechanic"), new ConvoyScope(ConvoyId), ScopedRequirements.Convoy,
            assignments: Leading(ConvoyId))).Should().BeFalse();
    }

    [Fact]
    public async Task A_resource_of_the_wrong_kind_for_the_requirement_is_denied()
    {
        (await AllowedAsync(ACaller("Loader", "ConvoyLeader"), new ConvoyScope(ConvoyId), ScopedRequirements.Location,
            assignments: Leading(ConvoyId))).Should().BeFalse();
    }

    [Fact]
    public async Task A_failing_assignment_store_is_never_a_success()
    {
        var assignments = Substitute.For<IScopeAssignments>();
        assignments.IsCurrentLeaderAsync(Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<bool>(_ => throw new InvalidOperationException("store down"));

        var act = () => AllowedAsync(ACaller("ConvoyLeader"), new ConvoyScope(ConvoyId), ScopedRequirements.Convoy,
            assignments: assignments);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}

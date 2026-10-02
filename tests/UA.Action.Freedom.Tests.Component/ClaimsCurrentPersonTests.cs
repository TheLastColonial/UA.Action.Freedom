using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// "Who is calling" is answered in one place. The answer is a linked volunteer or no one: there
/// is no "unknown" identity a write could be stamped with.
/// </summary>
public class ClaimsCurrentPersonTests
{
    private static readonly Guid PersonId = new("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

    private static PersonReadModel APerson() => new(
        PersonId, "Olena", "Shevchenko", new DateTime(1988, 4, 12, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2024, 2, 24, 0, 0, 0, DateTimeKind.Utc), null, false, false);

    private static ClaimsCurrentPerson ACaller(IPersonRepository people, params Claim[] claims)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) };
        return new ClaimsCurrentPerson(new HttpContextAccessor { HttpContext = context }, people);
    }

    [Fact]
    public async Task A_login_linked_to_a_volunteer_resolves_to_that_volunteer()
    {
        var people = new InMemoryPersonRepository(APerson()).LinkedTo("kc-1", PersonId);

        var current = await ACaller(people, new Claim("sub", "kc-1")).ResolveAsync(TestContext.Current.CancellationToken);

        current.Should().Be(new CurrentPerson.Linked(PersonId));
    }

    [Fact]
    public async Task The_name_identifier_claim_is_read_before_sub()
    {
        var people = new InMemoryPersonRepository(APerson()).LinkedTo("kc-1", PersonId);

        var current = await ACaller(
            people, new Claim(ClaimTypes.NameIdentifier, "kc-1"), new Claim("sub", "other"))
            .ResolveAsync(TestContext.Current.CancellationToken);

        current.Should().Be(new CurrentPerson.Linked(PersonId));
    }

    [Fact]
    public async Task A_login_nobody_linked_is_not_linked()
    {
        var people = new InMemoryPersonRepository(APerson());

        var current = await ACaller(people, new Claim("sub", "kc-1")).ResolveAsync(TestContext.Current.CancellationToken);

        current.Should().BeOfType<CurrentPerson.NotLinked>();
    }

    [Fact]
    public async Task An_erased_volunteers_login_is_not_linked()
    {
        var people = new InMemoryPersonRepository(APerson()).LinkedTo("kc-1", PersonId);
        await people.DeleteAsync(PersonId, TestContext.Current.CancellationToken);

        var current = await ACaller(people, new Claim("sub", "kc-1")).ResolveAsync(TestContext.Current.CancellationToken);

        current.Should().BeOfType<CurrentPerson.NotLinked>();
    }

    [Fact]
    public async Task A_token_with_no_subject_is_not_linked_rather_than_unknown()
    {
        var people = new InMemoryPersonRepository(APerson()).LinkedTo("unknown", PersonId);

        var current = await ACaller(people).ResolveAsync(TestContext.Current.CancellationToken);

        current.Should().BeOfType<CurrentPerson.NotLinked>();
    }
}

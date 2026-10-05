using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Linking a login to a volunteer, and <c>GET /me</c>, from the outside. Only an Administrator
/// links; every authenticated caller may ask who they are, and learns nothing but their own.
/// </summary>
public class MeEndpointTests
{
    private static readonly Guid Id = new("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

    private static PersonReadModel AStoredPerson(Guid? id = null) => new(
        id ?? Id, "Olena", "Shevchenko", new DateTime(1988, 4, 12, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2024, 2, 24, 0, 0, 0, DateTimeKind.Utc), "+447700900123", false, false);

    [Theory]
    [InlineData("Dispatcher")]
    [InlineData("Purchaser")]
    [InlineData("GroundOfficer")]
    public async Task Only_an_administrator_may_link_a_login(string role)
    {
        var people = new InMemoryPersonRepository(AStoredPerson());
        await using var api = FreedomApi.WithPeople(people, roles: role);
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync($"/people/{Id}/login", new { subject = "kc-1" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await people.FindBySubjectAsync("kc-1", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task An_administrator_links_a_login_to_a_volunteer()
    {
        var people = new InMemoryPersonRepository(AStoredPerson());
        await using var api = FreedomApi.WithPeople(people, roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync($"/people/{Id}/login", new { subject = "kc-1" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await people.FindBySubjectAsync("kc-1", TestContext.Current.CancellationToken)).Should().Be(Id);
    }

    [Fact]
    public async Task An_administrator_may_link_their_own_login_so_the_first_link_can_be_made()
    {
        var people = new InMemoryPersonRepository(AStoredPerson());
        await using var api = FreedomApi.WithPeople(people, roles: "Administrator");
        using var client = api.CreateClient();

        var me = await client.GetFromJsonAsync<JsonElement>("/me", TestContext.Current.CancellationToken);
        var response = await client.PutAsJsonAsync(
            $"/people/{Id}/login", new { subject = me.GetProperty("subject").GetString() }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var linked = await client.GetFromJsonAsync<JsonElement>("/me", TestContext.Current.CancellationToken);
        linked.GetProperty("personId").GetGuid().Should().Be(Id);
    }

    [Fact]
    public async Task Linking_a_login_already_linked_to_someone_else_is_a_conflict()
    {
        var other = Guid.NewGuid();
        var people = new InMemoryPersonRepository(AStoredPerson(), AStoredPerson(other)).LinkedTo("kc-1", other);
        await using var api = FreedomApi.WithPeople(people, roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync($"/people/{Id}/login", new { subject = "kc-1" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Linking_a_login_to_an_unknown_volunteer_is_not_found()
    {
        await using var api = FreedomApi.WithPeople(new InMemoryPersonRepository(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync($"/people/{Id}/login", new { subject = "kc-1" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Linking_a_blank_login_is_a_validation_problem()
    {
        var people = new InMemoryPersonRepository(AStoredPerson());
        await using var api = FreedomApi.WithPeople(people, roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync($"/people/{Id}/login", new { subject = "" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Me_without_a_token_is_unauthorized()
    {
        await using var api = FreedomApi.WithPeople(new InMemoryPersonRepository(), authenticated: false);
        using var client = api.CreateClient();

        var response = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_reports_an_unlinked_login_with_its_subject_and_roles_only()
    {
        await using var api = FreedomApi.WithPeople(new InMemoryPersonRepository(AStoredPerson()), roles: ["GroundOfficer"]);
        using var client = api.CreateClient();

        var me = await client.GetFromJsonAsync<JsonElement>("/me", TestContext.Current.CancellationToken);

        me.GetProperty("subject").GetString().Should().Be("test-user");
        me.GetProperty("roles").EnumerateArray().Select(role => role.GetString()).Should().Equal("GroundOfficer");
        me.GetProperty("personId").ValueKind.Should().Be(JsonValueKind.Null);
        me.GetProperty("displayName").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Me_reports_the_person_id_and_display_name_of_a_linked_login_and_nothing_more()
    {
        var people = new InMemoryPersonRepository(AStoredPerson()).WithTestUserLinkedTo(Id);
        await using var api = FreedomApi.WithPeople(people, roles: ["Loader"]);
        using var client = api.CreateClient();

        var me = await client.GetFromJsonAsync<JsonElement>("/me", TestContext.Current.CancellationToken);

        me.GetProperty("personId").GetGuid().Should().Be(Id);
        me.GetProperty("displayName").GetString().Should().Be("Olena Shevchenko");
        me.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("subject", "roles", "personId", "displayName", "ledConvoyIds", "managedLocationIds");
    }

    [Fact]
    public async Task Me_reports_the_locations_a_loader_manages_from_the_assignments_and_not_the_token()
    {
        var people = new InMemoryPersonRepository(AStoredPerson()).WithTestUserLinkedTo(Id);
        var loaders = new InMemoryLoaderAssignmentRepository().Managing(Id, 4).Managing(Id, 9);
        await using var api = FreedomApi.WithLocations(
            new InMemoryLocationRepository(), new InMemoryBayRepository(), loaders, people, roles: ["Loader"]);
        using var client = api.CreateClient();

        var me = await client.GetFromJsonAsync<JsonElement>("/me", TestContext.Current.CancellationToken);

        me.GetProperty("managedLocationIds").EnumerateArray().Select(id => id.GetInt32()).Should().Equal(4, 9);
        me.GetProperty("ledConvoyIds").GetArrayLength().Should().Be(0);
    }
}

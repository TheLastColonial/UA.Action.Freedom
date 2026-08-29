using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// The <c>/people</c> contract from the outside: status codes, the authorization split between
/// reads and writes, and validation. Persistence is faked
/// (<see cref="InMemoryPersonRepository"/>); the Dapper repository has its own tests.
/// </summary>
/// <remarks>
/// The roster is volunteer personal data. Writes are Administrator only — approving volunteers
/// and revoking access when they leave is the Administrator's job — while every operational role
/// reads, because a dispatcher builds driver teams and a loader needs to know who validated a
/// box. The Ground Officer is excluded from both, as it is everywhere outside the receiver
/// slice (docs/domain/key-concepts.md § Roles).
/// </remarks>
public class PersonEndpointTests
{
    private static readonly Guid Id = new("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

    private static PersonReadModel AStoredPerson(Guid? id = null, bool isDriver = false) => new(
        id ?? Id,
        "Olena",
        "Shevchenko",
        new DateTime(1988, 4, 12, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2024, 2, 24, 0, 0, 0, DateTimeKind.Utc),
        "+447700900123",
        isDriver,
        Committed: false);

    private static object ACreateBody(bool isDriver = false, bool committed = false) => new
    {
        firstName = "Olena",
        lastName = "Shevchenko",
        dateOfBirth = "1988-04-12T00:00:00Z",
        joined = "2024-02-24T00:00:00Z",
        phone = "+447700900123",
        isDriver,
        committed,
    };

    [Fact]
    public async Task Listing_volunteers_without_a_token_is_unauthorized()
    {
        await using var api = FreedomApi.WithPeople(new InMemoryPersonRepository(), authenticated: false);
        using var client = api.CreateClient();

        var response = await client.GetAsync("/people", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_ground_officer_is_refused_the_volunteer_roster()
    {
        await using var api = FreedomApi.WithPeople(new InMemoryPersonRepository(), roles: "GroundOfficer");
        using var client = api.CreateClient();

        var response = await client.GetAsync("/people", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_dispatcher_may_read_the_roster()
    {
        var repository = new InMemoryPersonRepository(AStoredPerson());
        await using var api = FreedomApi.WithPeople(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.GetAsync("/people", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_dispatcher_may_not_add_a_volunteer()
    {
        // Reading the roster and controlling who is on it are different privileges.
        var repository = new InMemoryPersonRepository();
        await using var api = FreedomApi.WithPeople(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/people", ACreateBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        repository.Count.Should().Be(0);
    }

    [Fact]
    public async Task An_administrator_adds_a_volunteer_and_gets_its_location_back()
    {
        var repository = new InMemoryPersonRepository();
        await using var api = FreedomApi.WithPeople(repository, roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/people", ACreateBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        repository.Count.Should().Be(1);

        // The identifier is minted by the application, so the only way a caller learns it is
        // the Location header.
        response.Headers.Location!.ToString().Should().EndWith($"/people/{repository.Single().Id}");
    }

    [Fact]
    public async Task Adding_a_volunteer_with_a_blank_name_is_a_validation_problem()
    {
        var repository = new InMemoryPersonRepository();
        await using var api = FreedomApi.WithPeople(repository, roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/people",
            new
            {
                firstName = "",
                lastName = "Shevchenko",
                dateOfBirth = "1988-04-12T00:00:00Z",
                joined = "2024-02-24T00:00:00Z",
                isDriver = false,
                committed = false,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("errors").TryGetProperty("FirstName", out _).Should().BeTrue();
        repository.Count.Should().Be(0);
    }

    [Fact]
    public async Task Committing_a_volunteer_who_does_not_drive_is_a_validation_problem()
    {
        // Otherwise a non-driver appears on the dispatcher's committed-driver shortlist.
        var repository = new InMemoryPersonRepository();
        await using var api = FreedomApi.WithPeople(repository, roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/people",
            ACreateBody(isDriver: false, committed: true),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        repository.Count.Should().Be(0);
    }

    [Fact]
    public async Task A_validation_problem_never_echoes_the_personal_data_it_rejected()
    {
        // Validation responses reach client logs and browser consoles. Volunteer personal data
        // must not travel with them (docs/recommendations.md §4.8).
        await using var api = FreedomApi.WithPeople(new InMemoryPersonRepository(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/people",
            new
            {
                firstName = "",
                lastName = "Shevchenko",
                dateOfBirth = "1988-04-12T00:00:00Z",
                joined = "2024-02-24T00:00:00Z",
                phone = "+447700900123",
                isDriver = false,
                committed = false,
            },
            TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.Should().NotContain("+447700900123").And.NotContain("1988-04-12");
    }

    [Fact]
    public async Task Fetching_a_volunteer_returns_their_details()
    {
        var repository = new InMemoryPersonRepository(AStoredPerson(isDriver: true));
        await using var api = FreedomApi.WithPeople(repository, roles: "Loader");
        using var client = api.CreateClient();

        var person = await client.GetFromJsonAsync<JsonElement>($"/people/{Id}", TestContext.Current.CancellationToken);

        person.GetProperty("lastName").GetString().Should().Be("Shevchenko");
        person.GetProperty("isDriver").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Fetching_an_unknown_volunteer_is_a_404()
    {
        await using var api = FreedomApi.WithPeople(new InMemoryPersonRepository(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/people/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Listing_drivers_only_leaves_out_everyone_who_does_not_drive()
    {
        var driver = AStoredPerson(Guid.NewGuid(), isDriver: true);
        var repository = new InMemoryPersonRepository(AStoredPerson(isDriver: false), driver);
        await using var api = FreedomApi.WithPeople(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var people = await client.GetFromJsonAsync<JsonElement>(
            "/people?driversOnly=true", TestContext.Current.CancellationToken);

        people.EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("id").GetGuid().Should().Be(driver.Id);
    }

    [Fact]
    public async Task An_administrator_updates_a_volunteer()
    {
        var repository = new InMemoryPersonRepository(AStoredPerson());
        await using var api = FreedomApi.WithPeople(repository, roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/people/{Id}",
            new
            {
                firstName = "Olena",
                lastName = "Shevchenko-Bell",
                dateOfBirth = "1988-04-12T00:00:00Z",
                joined = "2024-02-24T00:00:00Z",
                phone = "+447700900123",
                isDriver = true,
                committed = true,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await repository.GetByIdAsync(Id, TestContext.Current.CancellationToken);
        stored!.LastName.Should().Be("Shevchenko-Bell");
        stored.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Updating_an_unknown_volunteer_is_a_404()
    {
        await using var api = FreedomApi.WithPeople(new InMemoryPersonRepository(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/people/{Guid.NewGuid()}",
            new
            {
                firstName = "Olena",
                lastName = "Shevchenko",
                dateOfBirth = "1988-04-12T00:00:00Z",
                joined = "2024-02-24T00:00:00Z",
                isDriver = false,
                committed = false,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_administrator_removes_a_volunteer_who_has_left()
    {
        var repository = new InMemoryPersonRepository(AStoredPerson());
        await using var api = FreedomApi.WithPeople(repository, roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync($"/people/{Id}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        repository.Contains(Id).Should().BeFalse();
    }

    [Fact]
    public async Task Removing_an_unknown_volunteer_is_a_404()
    {
        await using var api = FreedomApi.WithPeople(new InMemoryPersonRepository(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync($"/people/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

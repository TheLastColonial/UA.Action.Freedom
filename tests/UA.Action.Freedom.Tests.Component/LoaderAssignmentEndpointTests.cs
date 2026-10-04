using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Locations;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// <c>/locations/{id}/loaders</c> from the outside: the Administrator alone assigns a Loader to a location (O31), the
/// history is kept, and an assignment is by person so it can precede the login link.
/// </summary>
public class LoaderAssignmentEndpointTests
{
    private const int LocationId = 3;

    private static readonly Guid Olena = new("0b7e8f2a-4c1d-4e5f-9a6b-7c8d9e0f1a2b");

    private static LocationReadModel AStoredLocation() => new(
        LocationId, "Coventry Depot", "Unit 4", "Cross Road", "Coventry", "United Kingdom", "CV1 2AB");

    private static PersonReadModel ALoader() => new(
        Olena, "Olena", "Bondar", new DateTime(1988, 4, 12, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2024, 2, 24, 0, 0, 0, DateTimeKind.Utc), null, false, false);

    private static WebApplicationFactoryHolder Api(string role, InMemoryLoaderAssignmentRepository? loaders = null) =>
        new(loaders ?? new InMemoryLoaderAssignmentRepository(), role);

    [Fact]
    public async Task An_administrator_assigns_a_loader_to_a_location()
    {
        var loaders = new InMemoryLoaderAssignmentRepository();
        await using var holder = Api("Administrator", loaders);
        using var client = holder.Client();

        var response = await client.PutAsync($"/locations/{LocationId}/loaders/{Olena}", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await loaders.ManagesAsync(Olena, LocationId, TestContext.Current.CancellationToken)).Should().BeTrue();
        var listed = await client.GetFromJsonAsync<JsonElement>($"/locations/{LocationId}/loaders", TestContext.Current.CancellationToken);
        listed.GetArrayLength().Should().Be(1);
        listed[0].GetProperty("personId").GetGuid().Should().Be(Olena);
        listed[0].GetProperty("until").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("Dispatcher")]
    [InlineData("Loader")]
    [InlineData("Purchaser")]
    [InlineData("GroundOfficer")]
    public async Task No_other_role_assigns_reads_or_removes_a_loader(string role)
    {
        var loaders = new InMemoryLoaderAssignmentRepository().Managing(Olena, LocationId);
        await using var holder = Api(role, loaders);
        using var client = holder.Client();
        var token = TestContext.Current.CancellationToken;

        (await client.PutAsync($"/locations/{LocationId}/loaders/{Guid.NewGuid()}", null, token))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/locations/{LocationId}/loaders", token))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync($"/locations/{LocationId}/loaders/{Olena}", token))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await loaders.ManagesAsync(Olena, LocationId, token)).Should().BeTrue();
    }

    [Fact]
    public async Task Assigning_the_same_loader_twice_is_a_conflict()
    {
        var loaders = new InMemoryLoaderAssignmentRepository().Managing(Olena, LocationId);
        await using var holder = Api("Administrator", loaders);
        using var client = holder.Client();

        var response = await client.PutAsync($"/locations/{LocationId}/loaders/{Olena}", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Assigning_to_an_unknown_location_is_not_found_and_an_unknown_person_is_unprocessable()
    {
        await using var holder = Api("Administrator");
        using var client = holder.Client();
        var token = TestContext.Current.CancellationToken;

        (await client.PutAsync($"/locations/99/loaders/{Olena}", null, token))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PutAsync($"/locations/{LocationId}/loaders/{Guid.NewGuid()}", null, token))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Removing_a_loader_closes_the_assignment_and_keeps_it_in_the_history()
    {
        var loaders = new InMemoryLoaderAssignmentRepository().Managing(Olena, LocationId);
        await using var holder = Api("Administrator", loaders);
        using var client = holder.Client();
        var token = TestContext.Current.CancellationToken;

        var response = await client.DeleteAsync($"/locations/{LocationId}/loaders/{Olena}", token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await loaders.ManagesAsync(Olena, LocationId, token)).Should().BeFalse();
        var listed = await client.GetFromJsonAsync<JsonElement>($"/locations/{LocationId}/loaders", token);
        listed.GetArrayLength().Should().Be(1);
        listed[0].GetProperty("until").ValueKind.Should().NotBe(JsonValueKind.Null);
        (await client.DeleteAsync($"/locations/{LocationId}/loaders/{Olena}", token))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_loader_removed_and_assigned_again_has_two_rows_and_one_open()
    {
        var loaders = new InMemoryLoaderAssignmentRepository().Managing(Olena, LocationId);
        await using var holder = Api("Administrator", loaders);
        using var client = holder.Client();
        var token = TestContext.Current.CancellationToken;
        await client.DeleteAsync($"/locations/{LocationId}/loaders/{Olena}", token);

        await client.PutAsync($"/locations/{LocationId}/loaders/{Olena}", null, token);

        var listed = await client.GetFromJsonAsync<JsonElement>($"/locations/{LocationId}/loaders", token);
        listed.GetArrayLength().Should().Be(2);
        listed.EnumerateArray().Count(row => row.GetProperty("until").ValueKind == JsonValueKind.Null).Should().Be(1);
    }

    private sealed class WebApplicationFactoryHolder : IAsyncDisposable
    {
        private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> api;

        public WebApplicationFactoryHolder(InMemoryLoaderAssignmentRepository loaders, string role) =>
            api = FreedomApi.WithLocations(
                new InMemoryLocationRepository(AStoredLocation()),
                new InMemoryBayRepository(),
                loaders,
                InMemoryPersonRepository.WithLinkedTestUser(ALoader()),
                roles: role);

        public HttpClient Client() => api.CreateClient();

        public ValueTask DisposeAsync() => api.DisposeAsync();
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// <c>/convoys/{id}/leader</c> from the outside: one leader, a Driver crewed on the convoy, nominated by a Dispatcher
/// or Administrator, with the history kept (D8, D17, P7, P14).
/// </summary>
public class ConvoyLeaderEndpointTests
{
    private const int Id = 42;
    private const string Vin = "WVWZZZ1JZXW000001";

    private static readonly Guid Olena = new("0b7e8f2a-4c1d-4e5f-9a6b-7c8d9e0f1a2b");
    private static readonly Guid Taras = new("1c8f9a3b-5d2e-4f60-8b7c-8d9e0f1a2b3c");
    private static readonly Guid Ivana = new("2d9a0b4c-6e3f-4071-9c8d-9e0f1a2b3c4d");

    private static InMemoryConvoyRepository AConvoyWithACrew() =>
        new InMemoryConvoyRepository(
                new ConvoyReadModel(Id, new DateTime(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 5, 18, 0, 0, DateTimeKind.Utc), null))
            .WithVehicle(Vin, onConvoy: Id)
            .WithPerson(Olena, "Olena", "Bondar")
            .WithPerson(Taras, "Taras", "Melnyk")
            .WithPerson(Ivana, "Ivana", "Kovalenko")
            .WithCrew(Vin, Olena)
            .WithCrew(Vin, Taras)
            .WithCrew(Vin, Ivana, CrewRole.Passenger);

    private static Task<HttpResponseMessage> NominateAsync(HttpClient client, Guid personId) =>
        client.PutAsJsonAsync($"/convoys/{Id}/leader", new { personId }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("Dispatcher")]
    [InlineData("Administrator")]
    public async Task A_dispatcher_or_administrator_nominates_a_crewed_driver(string role)
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithACrew(), roles: role);
        using var client = api.CreateClient();

        var response = await NominateAsync(client, Olena);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var leader = await client.GetFromJsonAsync<JsonElement>($"/convoys/{Id}/leader", TestContext.Current.CancellationToken);
        leader.GetProperty("current").GetProperty("personId").GetGuid().Should().Be(Olena);
        leader.GetProperty("current").GetProperty("personName").GetString().Should().Be("Olena Bondar");
        leader.GetProperty("current").GetProperty("until").ValueKind.Should().Be(JsonValueKind.Null);
        leader.GetProperty("history").GetArrayLength().Should().Be(1);
    }

    [Theory]
    [InlineData("Loader")]
    [InlineData("Purchaser")]
    [InlineData("GroundOfficer")]
    public async Task Other_roles_cannot_nominate_a_leader(string role)
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithACrew(), roles: role);
        using var client = api.CreateClient();

        var response = await NominateAsync(client, Olena);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reassigning_closes_the_old_assignment_and_keeps_both_in_the_history()
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithACrew(), roles: "Dispatcher");
        using var client = api.CreateClient();
        await NominateAsync(client, Olena);

        var response = await NominateAsync(client, Taras);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var leader = await client.GetFromJsonAsync<JsonElement>($"/convoys/{Id}/leader", TestContext.Current.CancellationToken);
        leader.GetProperty("current").GetProperty("personId").GetGuid().Should().Be(Taras);
        var history = leader.GetProperty("history").EnumerateArray().ToList();
        history.Select(a => a.GetProperty("personId").GetGuid()).Should().Equal(Taras, Olena);
        history[1].GetProperty("until").ValueKind.Should().Be(JsonValueKind.String);
        history.Count(a => a.GetProperty("until").ValueKind == JsonValueKind.Null).Should().Be(1);
    }

    [Fact]
    public async Task Someone_not_on_the_crew_and_a_passenger_are_both_refused()
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithACrew(), roles: "Dispatcher");
        using var client = api.CreateClient();

        (await NominateAsync(client, Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await NominateAsync(client, Ivana)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var leader = await client.GetFromJsonAsync<JsonElement>($"/convoys/{Id}/leader", TestContext.Current.CancellationToken);
        leader.GetProperty("current").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Nominating_the_sitting_leader_again_is_a_conflict()
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithACrew(), roles: "Dispatcher");
        using var client = api.CreateClient();
        await NominateAsync(client, Olena);

        (await NominateAsync(client, Olena)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_leader_of_an_unknown_convoy_is_not_found()
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithACrew(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.GetAsync("/convoys/999/leader", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_leader_cannot_be_taken_off_the_crew_until_another_is_nominated()
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithACrew(), roles: "Dispatcher");
        using var client = api.CreateClient();
        await NominateAsync(client, Olena);

        var refused = await client.DeleteAsync(
            $"/convoys/{Id}/vehicles/{Vin}/crew/{Olena}", TestContext.Current.CancellationToken);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await NominateAsync(client, Taras);
        var allowed = await client.DeleteAsync(
            $"/convoys/{Id}/vehicles/{Vin}/crew/{Olena}", TestContext.Current.CancellationToken);
        allowed.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}

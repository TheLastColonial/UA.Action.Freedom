using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// O13: equipment the charity buys for a vehicle is a step of creating a convoy. It has no donor, is accounted for
/// separately from donations, and its cost counts under the budget's Other line.
/// </summary>
public class ConvoyEquipmentEndpointTests
{
    private const int ConvoyId = 7;
    private const string Vin = "WVWZZZ1JZXW000001";

    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    private static InMemoryConvoyRepository AConvoy(DateTime? arrivedAt = null) =>
        new InMemoryConvoyRepository(new ConvoyReadModel(ConvoyId, Departs, Departs.AddDays(4), null, ArrivedAt: arrivedAt))
            .WithVehicle(Vin, onConvoy: ConvoyId);

    private static string Route => $"/convoys/{ConvoyId}/vehicles/{Vin}/equipment";

    private static async Task<int> AddItemAsync(HttpClient client, string name, decimal? unitCost = null)
    {
        var response = await client.PostAsJsonAsync(
            "/equipment-items", new { name, unitCostGbp = unitCost }, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task A_dispatcher_catalogues_equipment_and_it_lists_by_name()
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();
        await AddItemAsync(client, "Warning triangle", 6.50m);
        await AddItemAsync(client, "Fire extinguisher");

        var items = await client.GetFromJsonAsync<JsonElement>("/equipment-items", TestContext.Current.CancellationToken);

        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("name").GetString().Should().Be("Fire extinguisher");
        items[0].GetProperty("unitCostGbp").ValueKind.Should().Be(JsonValueKind.Null);
        items[1].GetProperty("unitCostGbp").GetDecimal().Should().Be(6.50m);
    }

    [Fact]
    public async Task The_same_item_cannot_be_catalogued_twice()
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();
        await AddItemAsync(client, "Warning triangle");

        var again = await client.PostAsJsonAsync(
            "/equipment-items", new { name = "warning TRIANGLE" }, TestContext.Current.CancellationToken);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Equipment_is_put_on_a_vehicle_replaced_and_read_back_with_its_counted_cost()
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();
        var triangle = await AddItemAsync(client, "Warning triangle", 6.50m);
        var strap = await AddItemAsync(client, "Tow strap");

        var put = await client.PutAsJsonAsync(
            Route,
            new { lines = new object[] { new { equipmentItemId = triangle, quantity = 2 }, new { equipmentItemId = strap, quantity = 1, costGbp = 12m } } },
            TestContext.Current.CancellationToken);
        var lines = await client.GetFromJsonAsync<JsonElement>(Route, TestContext.Current.CancellationToken);

        put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        lines.GetArrayLength().Should().Be(2);
        var counted = lines.EnumerateArray().ToDictionary(
            line => line.GetProperty("name").GetString()!, line => line.GetProperty("countedCostGbp").GetDecimal());
        counted["Warning triangle"].Should().Be(13m);
        counted["Tow strap"].Should().Be(12m);

        await client.PutAsJsonAsync(
            Route, new { lines = new[] { new { equipmentItemId = strap, quantity = 4 } } }, TestContext.Current.CancellationToken);
        var replaced = await client.GetFromJsonAsync<JsonElement>(Route, TestContext.Current.CancellationToken);
        replaced.GetArrayLength().Should().Be(1);
        replaced[0].GetProperty("quantity").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task An_item_that_is_not_in_the_catalogue_changes_nothing()
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();
        var triangle = await AddItemAsync(client, "Warning triangle");
        await client.PutAsJsonAsync(
            Route, new { lines = new[] { new { equipmentItemId = triangle, quantity = 1 } } }, TestContext.Current.CancellationToken);

        var response = await client.PutAsJsonAsync(
            Route, new { lines = new[] { new { equipmentItemId = 999, quantity = 1 } } }, TestContext.Current.CancellationToken);
        var lines = await client.GetFromJsonAsync<JsonElement>(Route, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        lines.GetArrayLength().Should().Be(1);
    }

    [Theory]
    [InlineData("""{"lines":[{"equipmentItemId":1,"quantity":0}]}""")]
    [InlineData("""{"lines":[{"equipmentItemId":1,"quantity":1},{"equipmentItemId":1,"quantity":2}]}""")]
    [InlineData("""{"lines":[{"equipmentItemId":1,"quantity":1,"costGbp":-1}]}""")]
    public async Task Equipment_with_no_quantity_a_repeated_item_or_a_negative_cost_is_refused(string body)
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsync(
            Route, new StringContent(body, System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_vehicle_that_is_not_on_the_convoy_has_no_equipment_and_takes_none()
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();
        var triangle = await AddItemAsync(client, "Warning triangle");
        var other = $"/convoys/{ConvoyId}/vehicles/NOTONTHECONVOY00001/equipment";

        var get = await client.GetAsync(other, TestContext.Current.CancellationToken);
        var put = await client.PutAsJsonAsync(
            other, new { lines = new[] { new { equipmentItemId = triangle, quantity = 1 } } }, TestContext.Current.CancellationToken);

        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
        put.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_arrived_convoy_takes_no_new_equipment()
    {
        await using var api = FreedomApi.WithConvoyBudget(
            AConvoy(arrivedAt: Departs.AddDays(4)), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var put = await client.PutAsJsonAsync(
            Route, new { lines = Array.Empty<object>() }, TestContext.Current.CancellationToken);

        put.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_budget_summary_counts_equipment_under_Other_and_shows_it_separately()
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();
        var triangle = await AddItemAsync(client, "Warning triangle", 6.50m);
        await client.PutAsJsonAsync(
            Route, new { lines = new[] { new { equipmentItemId = triangle, quantity = 2 } } }, TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync(
            $"/convoys/{ConvoyId}/costs", new { type = "Other", amountGbp = 7m }, TestContext.Current.CancellationToken);

        var summary = await client.GetFromJsonAsync<JsonElement>(
            $"/convoys/{ConvoyId}/budget/summary", TestContext.Current.CancellationToken);

        summary.GetProperty("equipmentGbp").GetDecimal().Should().Be(13m);
        var other = summary.GetProperty("lines").EnumerateArray().Single(line => line.GetProperty("type").GetString() == "Other");
        other.GetProperty("actualGbp").GetDecimal().Should().Be(20m);
    }

    [Theory]
    [InlineData("Dispatcher", HttpStatusCode.NoContent)]
    [InlineData("Loader", HttpStatusCode.Forbidden)]
    [InlineData("Mechanic", HttpStatusCode.Forbidden)]
    [InlineData("GroundOfficer", HttpStatusCode.Forbidden)]
    public async Task Only_an_administrator_or_dispatcher_changes_a_vehicles_equipment(string role, HttpStatusCode expected)
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: role);
        using var client = api.CreateClient();

        var put = await client.PutAsJsonAsync(Route, new { lines = Array.Empty<object>() }, TestContext.Current.CancellationToken);
        var get = await client.GetAsync(Route, TestContext.Current.CancellationToken);

        put.StatusCode.Should().Be(expected);
        (get.StatusCode == HttpStatusCode.Forbidden).Should().Be(role is "Mechanic" or "GroundOfficer");
    }
}

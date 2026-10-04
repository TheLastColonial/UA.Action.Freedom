using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// O12, P3, O37: a convoy has a budget line per cost type and costs are compared with each. Ferry, hotel and
/// insurance costs are read from their booking or policy, so they are never entered twice. None of it is required
/// to depart.
/// </summary>
public class ConvoyBudgetEndpointTests
{
    private const int ConvoyId = 7;
    private const string Vin = "WVWZZZ1JZXW000001";

    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    private static InMemoryConvoyRepository AConvoy() =>
        new InMemoryConvoyRepository(new ConvoyReadModel(ConvoyId, Departs, Departs.AddDays(4), null))
            .WithVehicle(Vin, onConvoy: ConvoyId);

    private static string Budget => $"/convoys/{ConvoyId}/budget";

    private static string Costs => $"/convoys/{ConvoyId}/costs";

    private static object AFuelBudget(decimal planned = 1_000m) => new
    {
        lines = new object[] { new { type = "Fuel", plannedGbp = planned }, new { type = "Ferry", plannedGbp = 600m } },
    };

    [Fact]
    public async Task A_dispatcher_sets_the_budget_and_it_reads_back_by_cost_type()
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var put = await client.PutAsJsonAsync(Budget, AFuelBudget(), TestContext.Current.CancellationToken);
        var lines = await client.GetFromJsonAsync<JsonElement>(Budget, TestContext.Current.CancellationToken);

        put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        lines.GetArrayLength().Should().Be(2);
        lines[0].GetProperty("type").GetString().Should().Be("Fuel");
        lines[0].GetProperty("plannedGbp").GetDecimal().Should().Be(1_000m);
        lines[0].GetProperty("lastChangedByName").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Setting_the_budget_again_replaces_every_line()
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();
        await client.PutAsJsonAsync(Budget, AFuelBudget(), TestContext.Current.CancellationToken);

        await client.PutAsJsonAsync(
            Budget, new { lines = new[] { new { type = "Hotel", plannedGbp = 300m } } }, TestContext.Current.CancellationToken);
        var lines = await client.GetFromJsonAsync<JsonElement>(Budget, TestContext.Current.CancellationToken);

        lines.GetArrayLength().Should().Be(1);
        lines[0].GetProperty("type").GetString().Should().Be("Hotel");
    }

    [Fact]
    public async Task A_convoy_with_no_budget_reads_as_an_empty_list_and_an_unknown_convoy_is_not_found()
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var empty = await client.GetFromJsonAsync<JsonElement>(Budget, TestContext.Current.CancellationToken);
        var unknown = await client.GetAsync("/convoys/999/budget", TestContext.Current.CancellationToken);
        var put = await client.PutAsJsonAsync("/convoys/999/budget", AFuelBudget(), TestContext.Current.CancellationToken);

        empty.GetArrayLength().Should().Be(0);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        put.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("""{"lines":[{"type":"Fuel","plannedGbp":-1}]}""")]
    [InlineData("""{"lines":[{"type":"Fuel","plannedGbp":10},{"type":"Fuel","plannedGbp":20}]}""")]
    [InlineData("""{"lines":[{"type":"Fuel","plannedGbp":10.123}]}""")]
    [InlineData("""{"lines":[{"type":"Petrol","plannedGbp":10}]}""")]
    public async Task A_budget_with_a_negative_a_repeated_type_or_a_part_penny_is_refused(string body)
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsync(
            Budget, new StringContent(body, System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_fuel_cost_is_entered_against_a_vehicle_listed_and_deleted()
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var post = await client.PostAsJsonAsync(
            Costs, new { type = "Fuel", amountGbp = 412.30m, vin = Vin, note = "Diesel, Calais" }, TestContext.Current.CancellationToken);
        var created = await post.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var costs = await client.GetFromJsonAsync<JsonElement>(Costs, TestContext.Current.CancellationToken);
        var delete = await client.DeleteAsync($"{Costs}/{created.GetProperty("id").GetInt32()}", TestContext.Current.CancellationToken);
        var after = await client.GetFromJsonAsync<JsonElement>(Costs, TestContext.Current.CancellationToken);

        post.StatusCode.Should().Be(HttpStatusCode.Created);
        costs.GetArrayLength().Should().Be(1);
        costs[0].GetProperty("type").GetString().Should().Be("Fuel");
        costs[0].GetProperty("amountGbp").GetDecimal().Should().Be(412.30m);
        costs[0].GetProperty("vin").GetString().Should().Be(Vin);
        costs[0].GetProperty("lastChangedByName").GetString().Should().NotBeNullOrEmpty();
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        after.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Deleting_a_cost_that_is_not_on_this_convoy_is_not_found()
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync($"{Costs}/99", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("Ferry")]
    [InlineData("Hotel")]
    [InlineData("Insurance")]
    public async Task A_cost_held_on_a_booking_cannot_be_entered_again(string type)
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            Costs, new { type, amountGbp = 100m }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_cost_against_a_vehicle_not_on_the_convoy_is_not_found()
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            Costs, new { type = "Fuel", amountGbp = 50m, vin = "NOTONTHECONVOY00001" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_summary_derives_a_ferry_cost_from_the_booking_and_flags_a_line_that_is_over()
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();
        await client.PutAsJsonAsync(Budget, AFuelBudget(1_000m), TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync(Costs, new { type = "Fuel", amountGbp = 1_100m }, TestContext.Current.CancellationToken);
        await client.PutAsJsonAsync(
            $"/convoys/{ConvoyId}/vehicles/{Vin}/ferry",
            new { @operator = "P&O", reference = "POF-1", sailingAt = "2026-09-02T07:30:00Z", costGbp = 310m },
            TestContext.Current.CancellationToken);

        var summary = await client.GetFromJsonAsync<JsonElement>($"{Budget}/summary", TestContext.Current.CancellationToken);

        summary.GetProperty("budgetSet").GetBoolean().Should().BeTrue();
        summary.GetProperty("anyOverBudget").GetBoolean().Should().BeTrue();
        summary.GetProperty("actualTotalGbp").GetDecimal().Should().Be(1_410m);
        var fuel = summary.GetProperty("lines").EnumerateArray().Single(line => line.GetProperty("type").GetString() == "Fuel");
        fuel.GetProperty("overBudget").GetBoolean().Should().BeTrue();
        var ferry = summary.GetProperty("lines").EnumerateArray().Single(line => line.GetProperty("type").GetString() == "Ferry");
        ferry.GetProperty("actualGbp").GetDecimal().Should().Be(310m);
        ferry.GetProperty("plannedGbp").GetDecimal().Should().Be(600m);
        ferry.GetProperty("overBudget").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Readiness_advises_when_no_budget_is_set_and_when_a_line_is_over_but_never_blocks()
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var unset = await client.GetFromJsonAsync<JsonElement>($"/convoys/{ConvoyId}/readiness", TestContext.Current.CancellationToken);
        await client.PutAsJsonAsync(Budget, AFuelBudget(1_000m), TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync(Costs, new { type = "Fuel", amountGbp = 1_100m }, TestContext.Current.CancellationToken);
        var over = await client.GetFromJsonAsync<JsonElement>($"/convoys/{ConvoyId}/readiness", TestContext.Current.CancellationToken);

        unset.GetProperty("advisories").EnumerateArray().Select(a => a.GetString()).Should().Equal("No budget set");
        over.GetProperty("advisories").EnumerateArray().Select(a => a.GetString()).Should().Equal("Fuel is over budget");
        over.GetProperty("reasons").EnumerateArray().Select(a => a.GetString()).Should().NotContain(
            reason => reason!.Contains("budget", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_summary_of_a_convoy_without_a_budget_says_so()
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var summary = await client.GetFromJsonAsync<JsonElement>($"{Budget}/summary", TestContext.Current.CancellationToken);

        summary.GetProperty("budgetSet").GetBoolean().Should().BeFalse();
        summary.GetProperty("anyOverBudget").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("Administrator", HttpStatusCode.NoContent)]
    [InlineData("Dispatcher", HttpStatusCode.NoContent)]
    [InlineData("Loader", HttpStatusCode.Forbidden)]
    [InlineData("Purchaser", HttpStatusCode.Forbidden)]
    [InlineData("Mechanic", HttpStatusCode.Forbidden)]
    [InlineData("GroundOfficer", HttpStatusCode.Forbidden)]
    public async Task Only_an_administrator_or_dispatcher_can_set_the_budget(string role, HttpStatusCode expected)
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: role);
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(Budget, AFuelBudget(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData("Loader", HttpStatusCode.OK)]
    [InlineData("Purchaser", HttpStatusCode.OK)]
    [InlineData("Mechanic", HttpStatusCode.Forbidden)]
    [InlineData("GroundOfficer", HttpStatusCode.Forbidden)]
    public async Task The_budget_summary_and_costs_are_read_by_the_operational_roles_only(string role, HttpStatusCode expected)
    {
        await using var api = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: role);
        using var client = api.CreateClient();

        (await client.GetAsync($"{Budget}/summary", TestContext.Current.CancellationToken)).StatusCode.Should().Be(expected);
        (await client.GetAsync(Costs, TestContext.Current.CancellationToken)).StatusCode.Should().Be(expected);
        (await client.GetAsync(Budget, TestContext.Current.CancellationToken)).StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task Entering_a_cost_needs_the_write_policy_and_a_login()
    {
        await using var loader = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), roles: "Loader");
        await using var anonymous = FreedomApi.WithConvoyBudget(AConvoy(), new InMemoryConvoyBudgetRepository(), authenticated: false);
        var body = new { type = "Fuel", amountGbp = 10m };

        var forbidden = await loader.CreateClient().PostAsJsonAsync(Costs, body, TestContext.Current.CancellationToken);
        var unauthorised = await anonymous.CreateClient().PostAsJsonAsync(Costs, body, TestContext.Current.CancellationToken);

        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        unauthorised.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

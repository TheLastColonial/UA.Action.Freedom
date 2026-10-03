using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Donations;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// The <c>/donors</c> and <c>/donations</c> contract from the outside: who may read, who may write, who may erase,
/// what an erased donor looks like, and what the donor status report leaves out. Persistence is faked
/// (<see cref="InMemoryDonationRepository"/>); the Dapper repositories have their own tests.
/// </summary>
/// <remarks>
/// A donor is a person whose details are held for UK data protection, so reads are open to the operational roles
/// who take donations in, writes to the roles who enter them (O22), and erasure to the Administrator alone, as for
/// volunteers. The Ground Officer is excluded throughout: that role sees destinations, never donors.
/// </remarks>
public class DonationEndpointTests
{
    private static readonly Guid DonorId = new("2b1f6c1e-52a1-4a47-9d0c-6f3a1b7d9c01");

    private const int DonationId = 41;

    private static readonly DateOnly ReceivedOn = new(2026, 9, 20);

    private static DonorReadModel ADonor() => new(DonorId, "Margaret Hollis", "margaret@example.org", "+447700900456");

    private static InMemoryDonationRepository AStoreWithADonor() => new InMemoryDonationRepository().WithDonor(ADonor());

    private static InMemoryDonationRepository AStoreWithADonation() =>
        AStoreWithADonor().WithDonation(DonationId, DonorId, ReceivedOn, "Two boxes of tins");

    private static object ADonorBody(string name = "Margaret Hollis") => new
    {
        name,
        email = "margaret@example.org",
        phone = "+447700900456",
    };

    private static object ADonationBody(Guid? donorId = null) => new
    {
        donorId = donorId ?? DonorId,
        receivedOn = "2026-09-20",
        notes = "Two boxes of tins",
    };

    [Fact]
    public async Task Reading_donors_without_a_token_is_unauthorized()
    {
        await using var api = FreedomApi.WithDonations(AStoreWithADonor(), authenticated: false);
        using var client = api.CreateClient();

        var response = await client.GetAsync("/donors", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/donors")]
    [InlineData("/donors/2b1f6c1e-52a1-4a47-9d0c-6f3a1b7d9c01")]
    [InlineData("/donors/2b1f6c1e-52a1-4a47-9d0c-6f3a1b7d9c01/donations")]
    [InlineData("/donors/2b1f6c1e-52a1-4a47-9d0c-6f3a1b7d9c01/report")]
    [InlineData("/donations")]
    [InlineData("/donations/41")]
    public async Task A_ground_officer_may_not_read_donors_or_donations(string route)
    {
        await using var api = FreedomApi.WithDonations(AStoreWithADonation(), roles: "GroundOfficer");
        using var client = api.CreateClient();

        var response = await client.GetAsync(route, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Purchaser")]
    [InlineData("Dispatcher")]
    [InlineData("Loader")]
    public async Task Every_operational_role_may_read_donors_and_donations(string role)
    {
        await using var api = FreedomApi.WithDonations(AStoreWithADonation(), roles: role);
        using var client = api.CreateClient();

        (await client.GetAsync("/donors", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/donations", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Administrator", HttpStatusCode.Created)]
    [InlineData("Dispatcher", HttpStatusCode.Created)]
    [InlineData("Loader", HttpStatusCode.Created)]
    [InlineData("Purchaser", HttpStatusCode.Forbidden)]
    [InlineData("Mechanic", HttpStatusCode.Forbidden)]
    [InlineData("GroundOfficer", HttpStatusCode.Forbidden)]
    public async Task Entering_a_donor_is_for_the_roles_who_take_donations_in(string role, HttpStatusCode expected)
    {
        await using var api = FreedomApi.WithDonations(new InMemoryDonationRepository(), roles: role);
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/donors", ADonorBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task Creating_a_donor_returns_where_to_find_them_and_they_can_be_read_back()
    {
        var store = new InMemoryDonationRepository();
        await using var api = FreedomApi.WithDonations(store, roles: "Dispatcher");
        using var client = api.CreateClient();

        var created = await client.PostAsJsonAsync("/donors", ADonorBody(), TestContext.Current.CancellationToken);
        var read = await client.GetAsync(created.Headers.Location, TestContext.Current.CancellationToken);

        created.Headers.Location!.ToString().Should().StartWith("/donors/");
        var body = await read.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("name").GetString().Should().Be("Margaret Hollis");
        body.GetProperty("email").GetString().Should().Be("margaret@example.org");
    }

    [Fact]
    public async Task A_donor_needs_a_name()
    {
        await using var api = FreedomApi.WithDonations(new InMemoryDonationRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/donors", ADonorBody(name: ""), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_validation_message_names_the_field_and_never_quotes_the_value()
    {
        await using var api = FreedomApi.WithDonations(new InMemoryDonationRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/donors", new { name = "Margaret Hollis", email = "not-an-email-address", phone = (string?)null },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().NotContain("not-an-email-address");
    }

    [Fact]
    public async Task Updating_a_donor_who_is_not_on_file_is_not_found()
    {
        await using var api = FreedomApi.WithDonations(new InMemoryDonationRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync($"/donors/{DonorId}", ADonorBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("Administrator", HttpStatusCode.NoContent)]
    [InlineData("Dispatcher", HttpStatusCode.Forbidden)]
    [InlineData("Loader", HttpStatusCode.Forbidden)]
    [InlineData("GroundOfficer", HttpStatusCode.Forbidden)]
    public async Task Only_an_administrator_may_erase_a_donor(string role, HttpStatusCode expected)
    {
        await using var api = FreedomApi.WithDonations(AStoreWithADonor(), roles: role);
        using var client = api.CreateClient();

        var response = await client.DeleteAsync($"/donors/{DonorId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task Erasing_a_donor_who_is_not_on_file_is_not_found()
    {
        await using var api = FreedomApi.WithDonations(new InMemoryDonationRepository(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync($"/donors/{DonorId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Erasing_a_donor_is_never_refused_for_having_given()
    {
        await using var api = FreedomApi.WithDonations(
            AStoreWithADonation().WithItemsIn(DonationId), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync($"/donors/{DonorId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task An_erased_donor_cannot_be_read_but_their_donation_survives_under_Former_donor()
    {
        await using var api = FreedomApi.WithDonations(AStoreWithADonation(), roles: "Administrator");
        using var client = api.CreateClient();

        await client.DeleteAsync($"/donors/{DonorId}", TestContext.Current.CancellationToken);

        (await client.GetAsync($"/donors/{DonorId}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var donation = await client.GetFromJsonAsync<JsonElement>($"/donations/{DonationId}", TestContext.Current.CancellationToken);
        donation.GetProperty("donorName").GetString().Should().Be("Former donor");
        donation.GetProperty("notes").GetString().Should().Be("Two boxes of tins");
    }

    [Fact]
    public async Task Erasing_a_donor_does_not_change_what_their_report_totals()
    {
        var store = AStoreWithADonation()
            .WithReportItem(DonorId, new DonorReportItem(DonationId, ReceivedOn, "Tinned food", 12, 30m, false));
        await using var api = FreedomApi.WithDonations(store, roles: "Administrator");
        using var client = api.CreateClient();

        var before = await client.GetFromJsonAsync<JsonElement>($"/donors/{DonorId}/report", TestContext.Current.CancellationToken);
        await client.DeleteAsync($"/donors/{DonorId}", TestContext.Current.CancellationToken);
        var after = await client.GetFromJsonAsync<JsonElement>($"/donors/{DonorId}/report", TestContext.Current.CancellationToken);

        before.GetProperty("donorName").GetString().Should().Be("Margaret Hollis");
        after.GetProperty("donorName").GetString().Should().Be("Former donor");
        after.GetProperty("itemCount").GetInt32().Should().Be(before.GetProperty("itemCount").GetInt32());
        after.GetProperty("totalValueGbp").GetDecimal().Should().Be(before.GetProperty("totalValueGbp").GetDecimal());
    }

    [Fact]
    public async Task The_report_for_a_donor_who_was_never_on_file_is_not_found()
    {
        await using var api = FreedomApi.WithDonations(new InMemoryDonationRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/donors/{DonorId}/report", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_report_says_what_was_given_and_how_far_it_has_got_and_nothing_about_where_it_is_going()
    {
        var store = AStoreWithADonation()
            .WithReportItem(DonorId, new DonorReportItem(DonationId, ReceivedOn, "Tinned food", 12, 30m, true))
            .WithReportItem(DonorId, new DonorReportItem(DonationId, ReceivedOn, "Blankets", 4, null, false));
        await using var api = FreedomApi.WithDonations(store, roles: "Dispatcher");
        using var client = api.CreateClient();

        var body = await client.GetStringAsync($"/donors/{DonorId}/report", TestContext.Current.CancellationToken);

        using var report = JsonDocument.Parse(body);
        report.RootElement.GetProperty("itemCount").GetInt32().Should().Be(16);
        report.RootElement.GetProperty("totalValueGbp").GetDecimal().Should().Be(30m);
        report.RootElement.GetProperty("donations")[0].GetProperty("items")
            .EnumerateArray().Select(item => item.GetProperty("status").GetString())
            .Should().Equal("PackedAndChecked", "BeingPacked");

        var names = PropertyNames(report.RootElement);
        names.Should().NotContain(name =>
            name.Contains("receiver", StringComparison.OrdinalIgnoreCase)
            || name.Contains("region", StringComparison.OrdinalIgnoreCase)
            || name.Contains("route", StringComparison.OrdinalIgnoreCase)
            || name.Contains("address", StringComparison.OrdinalIgnoreCase)
            || name.Contains("location", StringComparison.OrdinalIgnoreCase)
            || name.Contains("convoy", StringComparison.OrdinalIgnoreCase)
            || name.Contains("manifest", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
            ? PropertyNames(property.Value).Prepend(property.Name)
            : [property.Name]),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => [],
    };

    [Fact]
    public async Task A_donation_is_recorded_against_a_donor_and_read_back_with_the_donor_name()
    {
        await using var api = FreedomApi.WithDonations(AStoreWithADonor(), roles: "Loader");
        using var client = api.CreateClient();

        var created = await client.PostAsJsonAsync("/donations", ADonationBody(), TestContext.Current.CancellationToken);
        var read = await client.GetFromJsonAsync<JsonElement>(created.Headers.Location, TestContext.Current.CancellationToken);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        read.GetProperty("donorName").GetString().Should().Be("Margaret Hollis");
        read.GetProperty("receivedOn").GetString().Should().Be("2026-09-20");
    }

    [Fact]
    public async Task A_donation_cannot_be_recorded_against_a_donor_who_is_not_on_file()
    {
        await using var api = FreedomApi.WithDonations(new InMemoryDonationRepository(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/donations", ADonationBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("type").GetString().Should().Be("donor-not-found");
    }

    [Fact]
    public async Task A_donation_needs_a_date()
    {
        await using var api = FreedomApi.WithDonations(AStoreWithADonor(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/donations", new { donorId = DonorId, notes = "No date" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_purchaser_may_not_record_a_donation()
    {
        await using var api = FreedomApi.WithDonations(AStoreWithADonor(), roles: "Purchaser");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/donations", ADonationBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Updating_a_donation_changes_its_date_and_notes()
    {
        await using var api = FreedomApi.WithDonations(AStoreWithADonation(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/donations/{DonationId}", new { receivedOn = "2026-09-22", notes = "Collected" }, TestContext.Current.CancellationToken);
        var read = await client.GetFromJsonAsync<JsonElement>($"/donations/{DonationId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        read.GetProperty("receivedOn").GetString().Should().Be("2026-09-22");
        read.GetProperty("notes").GetString().Should().Be("Collected");
    }

    [Fact]
    public async Task Updating_a_donation_that_does_not_exist_is_not_found()
    {
        await using var api = FreedomApi.WithDonations(new InMemoryDonationRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/donations/{DonationId}", new { receivedOn = "2026-09-22", notes = (string?)null }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_donation_that_items_are_packed_under_cannot_be_deleted()
    {
        await using var api = FreedomApi.WithDonations(AStoreWithADonation().WithItemsIn(DonationId), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync($"/donations/{DonationId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_unused_donation_can_be_deleted()
    {
        await using var api = FreedomApi.WithDonations(AStoreWithADonation(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync($"/donations/{DonationId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_donors_donations_are_listed_under_the_donor()
    {
        var store = AStoreWithADonation().WithDonation(42, Guid.NewGuid(), ReceivedOn);
        await using var api = FreedomApi.WithDonations(store, roles: "Dispatcher");
        using var client = api.CreateClient();

        var listed = await client.GetFromJsonAsync<JsonElement>($"/donors/{DonorId}/donations", TestContext.Current.CancellationToken);

        listed.EnumerateArray().Select(donation => donation.GetProperty("id").GetInt32()).Should().Equal(DonationId);
    }

    [Fact]
    public async Task Listing_donations_for_a_donor_who_is_not_on_file_is_not_found()
    {
        await using var api = FreedomApi.WithDonations(new InMemoryDonationRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/donors/{DonorId}/donations", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_donor_read_says_who_last_changed_them()
    {
        await using var api = FreedomApi.WithDonations(new InMemoryDonationRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var created = await client.PostAsJsonAsync("/donors", ADonorBody(), TestContext.Current.CancellationToken);
        var read = await client.GetFromJsonAsync<JsonElement>(created.Headers.Location, TestContext.Current.CancellationToken);

        read.GetProperty("lastChangedByName").GetString().Should().Be("Test User");
    }
}

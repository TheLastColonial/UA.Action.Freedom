using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Receivers;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// The <c>/receivers</c> contract from the outside, and the boundary running through it.
/// </summary>
/// <remarks>
/// This is the most security-sensitive surface in the API. A manifest listing precise Ukrainian
/// delivery addresses is a targeting document, and it crosses several borders where it may be
/// inspected or seized. So: <c>/receivers</c> is region-level and open to every operational
/// role, <c>/receivers/{ref}/detail</c> is Ground Officer alone, and every resolve is audited
/// (docs/domain/key-concepts.md § Data Sensitivity, recommendations §4.4).
/// </remarks>
public class ReceiverEndpointTests
{
    private static readonly Guid Ref = new("b3f1c4d2-5a6e-4f70-8901-2c3d4e5f6a7b");

    private const string ContactName = "Olena Kovalenko";
    private const string Street = "12 Vulytsia Sumska";
    private const string Phone = "+380501234567";

    private static ReceiverReadModel AReceiver() =>
        new(Ref, "Kharkiv Regional Hospital", "Kharkiv oblast");

    private static ReceiverDetailReadModel ADetail() =>
        new(Ref, ContactName, Phone, Street, null, "Kharkiv", "61002", null);

    private static object ADetailBody() => new
    {
        contactName = ContactName,
        contactPhone = Phone,
        addressLine1 = Street,
        city = "Kharkiv",
        postCode = "61002",
    };

    [Fact]
    public async Task Reading_receivers_without_a_token_is_unauthorized()
    {
        await using var api = FreedomApi.WithReceivers(
            new InMemoryReceiverRepository(), new InMemoryReceiverDetailRepository(), authenticated: false);
        using var client = api.CreateClient();

        var response = await client.GetAsync("/receivers", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_dispatcher_sees_the_organisation_and_the_region_and_nothing_else()
    {
        // This is the shape that may be printed on something crossing a border. If a street
        // ever appears in this payload, it appears on the manifest too.
        var api = FreedomApi.WithReceivers(
            new InMemoryReceiverRepository(AReceiver()),
            new InMemoryReceiverDetailRepository(ADetail()),
            roles: "Dispatcher");
        await using var _ = api;
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/receivers/{Ref}", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("Kharkiv oblast");
        body.Should().NotContain(Street).And.NotContain(ContactName).And.NotContain(Phone);
    }

    [Fact]
    public async Task The_receiver_list_never_carries_an_address()
    {
        var api = FreedomApi.WithReceivers(
            new InMemoryReceiverRepository(AReceiver()),
            new InMemoryReceiverDetailRepository(ADetail()),
            roles: "Loader");
        await using var _ = api;
        using var client = api.CreateClient();

        var response = await client.GetAsync("/receivers", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().NotContain(Street).And.NotContain(ContactName).And.NotContain(Phone);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Dispatcher")]
    [InlineData("Loader")]
    [InlineData("Purchaser")]
    public async Task No_role_but_the_ground_officer_may_resolve_a_delivery_address(string role)
    {
        // The Administrator is included on purpose. Administering access is not the same as
        // holding it, and §4.4 gives the address to one role only.
        var detail = new InMemoryReceiverDetailRepository(ADetail());
        var api = FreedomApi.WithReceivers(new InMemoryReceiverRepository(AReceiver()), detail, roles: role);
        await using var _ = api;
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/receivers/{Ref}/detail", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Refused before the repository was touched, so nothing was read and nothing is in the
        // trail: a 403 is not an access.
        detail.AccessLog.Should().BeEmpty();
    }

    [Fact]
    public async Task A_ground_officer_resolves_the_address()
    {
        var detail = new InMemoryReceiverDetailRepository(ADetail());
        var api = FreedomApi.WithReceivers(new InMemoryReceiverRepository(AReceiver()), detail, roles: "GroundOfficer");
        await using var _ = api;
        using var client = api.CreateClient();

        var resolved = await client.GetFromJsonAsync<JsonElement>(
            $"/receivers/{Ref}/detail", TestContext.Current.CancellationToken);

        resolved.GetProperty("addressLine1").GetString().Should().Be(Street);
        resolved.GetProperty("contactName").GetString().Should().Be(ContactName);
    }

    [Fact]
    public async Task Resolving_an_address_records_who_asked_and_why()
    {
        var detail = new InMemoryReceiverDetailRepository(ADetail());
        var api = FreedomApi.WithReceivers(new InMemoryReceiverRepository(AReceiver()), detail, roles: "GroundOfficer");
        await using var _ = api;
        using var client = api.CreateClient();

        await client.GetAsync(
            $"/receivers/{Ref}/detail?reason=Delivery%20scheduled%2012%20Sept", TestContext.Current.CancellationToken);

        var entry = detail.AccessLog.Should().ContainSingle().Subject;
        entry.Ref.Should().Be(Ref);
        entry.Reason.Should().Be("Delivery scheduled 12 Sept");

        // Identity comes from the token, never from the request — an audit trail the caller
        // could write their own name into would not be one.
        entry.PrincipalId.Should().Be("test-user");
    }

    [Fact]
    public async Task An_address_that_was_never_recorded_is_a_404_and_still_leaves_a_trail()
    {
        // A resolve attempt is worth seeing whether or not there was an address to return.
        var detail = new InMemoryReceiverDetailRepository();
        var api = FreedomApi.WithReceivers(new InMemoryReceiverRepository(AReceiver()), detail, roles: "GroundOfficer");
        await using var _ = api;
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/receivers/{Ref}/detail", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        detail.AccessLog.Should().ContainSingle();
    }

    [Fact]
    public async Task A_ground_officer_records_a_delivery_address()
    {
        var detail = new InMemoryReceiverDetailRepository();
        var api = FreedomApi.WithReceivers(new InMemoryReceiverRepository(AReceiver()), detail, roles: "GroundOfficer");
        await using var _ = api;
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/receivers/{Ref}/detail", ADetailBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        detail.Contains(Ref).Should().BeTrue();
    }

    [Fact]
    public async Task A_dispatcher_may_not_record_a_delivery_address()
    {
        var detail = new InMemoryReceiverDetailRepository();
        var api = FreedomApi.WithReceivers(new InMemoryReceiverRepository(AReceiver()), detail, roles: "Dispatcher");
        await using var _ = api;
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/receivers/{Ref}/detail", ADetailBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        detail.Contains(Ref).Should().BeFalse();
    }

    [Fact]
    public async Task Recording_an_address_for_an_unknown_receiver_is_a_404()
    {
        var detail = new InMemoryReceiverDetailRepository();
        var api = FreedomApi.WithReceivers(new InMemoryReceiverRepository(), detail, roles: "GroundOfficer");
        await using var _ = api;
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/receivers/{Ref}/detail", ADetailBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        detail.Contains(Ref).Should().BeFalse();
    }

    [Fact]
    public async Task A_rejected_address_is_never_echoed_back()
    {
        // A validation response for this body would otherwise put a Ukrainian street address
        // into whatever logs the client keeps.
        var api = FreedomApi.WithReceivers(
            new InMemoryReceiverRepository(AReceiver()), new InMemoryReceiverDetailRepository(), roles: "GroundOfficer");
        await using var _ = api;
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/receivers/{Ref}/detail",
            new { contactName = "", contactPhone = Phone, addressLine1 = Street, city = "Kharkiv" },
            TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().NotContain(Street).And.NotContain(Phone);
    }

    [Fact]
    public async Task A_ground_officer_registers_a_receiver()
    {
        var receivers = new InMemoryReceiverRepository();
        var api = FreedomApi.WithReceivers(receivers, new InMemoryReceiverDetailRepository(), roles: "GroundOfficer");
        await using var _ = api;
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/receivers",
            new { organisation = "Kharkiv Regional Hospital", region = "Kharkiv oblast" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        receivers.Count.Should().Be(1);
    }

    [Fact]
    public async Task A_loader_may_not_register_a_receiver()
    {
        var receivers = new InMemoryReceiverRepository();
        var api = FreedomApi.WithReceivers(receivers, new InMemoryReceiverDetailRepository(), roles: "Loader");
        await using var _ = api;
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/receivers",
            new { organisation = "Kharkiv Regional Hospital", region = "Kharkiv oblast" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        receivers.Count.Should().Be(0);
    }

    [Fact]
    public async Task Removing_a_receiver_removes_its_address_with_it()
    {
        // Deleting the reference while keeping the address would leave data held with nothing
        // pointing at it to say whose it is (§4.4.5).
        var receivers = new InMemoryReceiverRepository(AReceiver());
        var detail = new InMemoryReceiverDetailRepository(ADetail());
        var api = FreedomApi.WithReceivers(receivers, detail, roles: "GroundOfficer");
        await using var _ = api;
        using var client = api.CreateClient();

        var response = await client.DeleteAsync($"/receivers/{Ref}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        receivers.Contains(Ref).Should().BeFalse();
        detail.Contains(Ref).Should().BeFalse();
    }

    [Fact]
    public async Task An_administrator_may_not_remove_a_receiver_because_that_would_remove_an_address()
    {
        var receivers = new InMemoryReceiverRepository(AReceiver());
        var detail = new InMemoryReceiverDetailRepository(ADetail());
        var api = FreedomApi.WithReceivers(receivers, detail, roles: "Administrator");
        await using var _ = api;
        using var client = api.CreateClient();

        var response = await client.DeleteAsync($"/receivers/{Ref}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        receivers.Contains(Ref).Should().BeTrue();
        detail.Contains(Ref).Should().BeTrue();
    }
}

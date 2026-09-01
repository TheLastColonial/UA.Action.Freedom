using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Boxes;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// The <c>/boxes/{id}/qr-code</c>, <c>/label</c> and <c>/boxes/scan/{token}</c> contract from
/// the outside.
/// </summary>
/// <remarks>
/// A QR label ties the cardboard to its record: a scan of an active token resolves to the box.
/// Re-labelling revokes the previous token, so a lost label stops working. The printable label
/// crosses borders, so it carries a box number and nothing about the receiver — these tests
/// pin that (docs/domain/key-concepts.md § Box, § Data Sensitivity).
/// </remarks>
public class BoxQrCodeEndpointTests
{
    private const int BoxId = 7;

    private static BoxReadModel ABox(bool validated = false, Guid? receiverRef = null) => new(
        BoxId,
        WeightKg: validated ? 24 : 0,
        ReceiverRef: receiverRef,
        House: "Unit 4",
        Street: "Cross Road",
        City: "Coventry",
        Country: "United Kingdom",
        Postcode: "CV1 2AB",
        ValidatedByPersonId: validated ? Guid.NewGuid() : null,
        ValidatedAt: validated ? new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc) : null);

    private static InMemoryPersonRepository NoPeople() => new();

    private static string TokenFromLocation(HttpResponseMessage response) =>
        response.Headers.Location!.ToString().Split('/')[^1];

    [Fact]
    public async Task Reading_a_boxes_qr_code_without_a_token_is_unauthorized()
    {
        await using var api = FreedomApi.WithBoxes(
            new InMemoryBoxRepository(ABox()), NoPeople(), authenticated: false);
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/boxes/{BoxId}/qr-code", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_ground_officer_is_refused_a_box_label()
    {
        await using var api = FreedomApi.WithBoxes(
            new InMemoryBoxRepository(ABox()), NoPeople(), roles: "GroundOfficer");
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/boxes/{BoxId}/label", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_purchaser_may_read_a_label_but_not_issue_one()
    {
        var boxes = new InMemoryBoxRepository(ABox()).WithQrCode(
            new BoxQrCodeReadModel(Guid.NewGuid(), BoxId, DateTime.UtcNow, RevokedAt: null));
        await using var api = FreedomApi.WithBoxes(boxes, NoPeople(), roles: "Purchaser");
        using var client = api.CreateClient();

        (await client.GetAsync($"/boxes/{BoxId}/label", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var issue = await client.PostAsync($"/boxes/{BoxId}/qr-code", null, TestContext.Current.CancellationToken);

        issue.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_loader_issues_a_qr_code_and_it_resolves_to_the_box()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, NoPeople(), roles: "Loader");
        using var client = api.CreateClient();

        var issue = await client.PostAsync($"/boxes/{BoxId}/qr-code", null, TestContext.Current.CancellationToken);

        issue.StatusCode.Should().Be(HttpStatusCode.Created);
        var token = TokenFromLocation(issue);
        issue.Headers.Location!.ToString().Should().Be($"/boxes/scan/{token}");

        var resolved = await client.GetFromJsonAsync<JsonElement>(
            $"/boxes/scan/{token}", TestContext.Current.CancellationToken);
        resolved.GetProperty("id").GetInt32().Should().Be(BoxId);
    }

    [Fact]
    public async Task Issuing_a_qr_code_for_a_box_that_does_not_exist_is_a_404()
    {
        await using var api = FreedomApi.WithBoxes(new InMemoryBoxRepository(), NoPeople(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsync("/boxes/999/qr-code", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Re_issuing_a_qr_code_revokes_the_previous_label()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, NoPeople(), roles: "Loader");
        using var client = api.CreateClient();

        var first = TokenFromLocation(
            await client.PostAsync($"/boxes/{BoxId}/qr-code", null, TestContext.Current.CancellationToken));
        var second = TokenFromLocation(
            await client.PostAsync($"/boxes/{BoxId}/qr-code", null, TestContext.Current.CancellationToken));

        second.Should().NotBe(first);
        (await client.GetAsync($"/boxes/scan/{first}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/boxes/scan/{second}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Revoking_a_qr_code_makes_the_label_unresolvable()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, NoPeople(), roles: "Loader");
        using var client = api.CreateClient();

        var token = TokenFromLocation(
            await client.PostAsync($"/boxes/{BoxId}/qr-code", null, TestContext.Current.CancellationToken));

        var revoke = await client.DeleteAsync($"/boxes/{BoxId}/qr-code", TestContext.Current.CancellationToken);
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetAsync($"/boxes/{BoxId}/qr-code", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/boxes/scan/{token}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Revoking_when_there_is_no_qr_code_is_a_404()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, NoPeople(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync($"/boxes/{BoxId}/qr-code", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_printable_label_carries_the_box_number_and_not_the_receiver()
    {
        // The label is inspected at borders. A box bound for a named place must not say so.
        var receiver = Guid.NewGuid();
        var boxes = new InMemoryBoxRepository(ABox(receiverRef: receiver));
        await using var api = FreedomApi.WithBoxes(boxes, NoPeople(), roles: "Loader");
        using var client = api.CreateClient();

        await client.PostAsync($"/boxes/{BoxId}/qr-code", null, TestContext.Current.CancellationToken);

        var response = await client.GetAsync($"/boxes/{BoxId}/label", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/svg+xml");

        var svg = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        svg.Should().StartWith("<svg").And.Contain($"BOX #{BoxId}").And.Contain("UKRAINIAN ACTION");
        svg.Should().NotContain("Coventry").And.NotContain("Cross Road").And.NotContain(receiver.ToString());
    }

    [Fact]
    public async Task A_label_cannot_be_printed_for_a_box_with_no_qr_code()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, NoPeople(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/boxes/{BoxId}/label", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_qr_code_image_is_svg_by_default_and_png_on_request()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, NoPeople(), roles: "Loader");
        using var client = api.CreateClient();

        await client.PostAsync($"/boxes/{BoxId}/qr-code", null, TestContext.Current.CancellationToken);

        var svg = await client.GetAsync($"/boxes/{BoxId}/qr-code/image", TestContext.Current.CancellationToken);
        svg.Content.Headers.ContentType!.MediaType.Should().Be("image/svg+xml");
        (await svg.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().StartWith("<svg");

        var png = await client.GetAsync(
            $"/boxes/{BoxId}/qr-code/image?format=png", TestContext.Current.CancellationToken);
        png.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        var bytes = await png.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        bytes.Take(4).Should().Equal([(byte)0x89, (byte)0x50, (byte)0x4E, (byte)0x47]);
    }

    [Fact]
    public async Task The_qr_code_image_is_a_404_when_the_box_has_none()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, NoPeople(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.GetAsync(
            $"/boxes/{BoxId}/qr-code/image", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_validated_box_can_still_be_issued_and_printed_a_label()
    {
        var boxes = new InMemoryBoxRepository(ABox(validated: true));
        await using var api = FreedomApi.WithBoxes(boxes, NoPeople(), roles: "Loader");
        using var client = api.CreateClient();

        (await client.PostAsync($"/boxes/{BoxId}/qr-code", null, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.GetAsync($"/boxes/{BoxId}/label", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Scanning_an_unknown_token_is_a_404()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, NoPeople(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.GetAsync(
            $"/boxes/scan/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

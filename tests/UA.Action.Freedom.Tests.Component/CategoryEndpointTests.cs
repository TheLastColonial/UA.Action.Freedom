using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Categories;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// The <c>/categories</c> contract from the outside: the authorization split (every operational role reads, only
/// the Administrator writes, O31), the JSON shape, and the rules the fake shares with the SQL.
/// </summary>
public class CategoryEndpointTests
{
    private const int CategoryId = 2;

    private static ItemCategoryReadModel AStoredCategory(
        int id = CategoryId, string name = "Medicine", bool isFixed = true, string? euCode = "300490") => new(
        id, name, NameUk: "", isFixed, HazardClass: null, IsSensitive: true, IsNotCarried: false,
        WarnWithinDays: 180, UkCode: null, EuCode: euCode);

    private static object ACreateBody(string name = "Bedding") => new
    {
        nameEn = name,
        isSensitive = false,
        isNotCarried = false,
    };

    [Fact]
    public async Task Listing_categories_without_a_token_is_rejected()
    {
        await using var api = FreedomApi.WithCategories(new InMemoryItemCategoryRepository(), authenticated: false);
        using var client = api.CreateClient();

        (await client.GetAsync("/categories", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Purchaser")]
    [InlineData("Dispatcher")]
    [InlineData("Loader")]
    public async Task Every_operational_role_may_read_the_categories(string role)
    {
        await using var api = FreedomApi.WithCategories(
            new InMemoryItemCategoryRepository(AStoredCategory()), roles: role);
        using var client = api.CreateClient();

        (await client.GetAsync("/categories", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("GroundOfficer")]
    [InlineData("Mechanic")]
    public async Task Neither_a_ground_officer_nor_a_mechanic_reads_the_categories(string role)
    {
        await using var api = FreedomApi.WithCategories(new InMemoryItemCategoryRepository(), roles: role);
        using var client = api.CreateClient();

        (await client.GetAsync("/categories", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Dispatcher")]
    [InlineData("Loader")]
    [InlineData("Purchaser")]
    public async Task Only_the_administrator_writes_a_category(string role)
    {
        await using var api = FreedomApi.WithCategories(
            new InMemoryItemCategoryRepository(AStoredCategory()), roles: role);
        using var client = api.CreateClient();

        (await client.PostAsJsonAsync("/categories", ACreateBody(), TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync($"/categories/{CategoryId}", ACreateBody(), TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync(
            $"/categories/{CategoryId}/codes/EU", new { code = "300490" }, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_category_reads_back_with_its_codes_and_shelf_life_rule()
    {
        await using var api = FreedomApi.WithCategories(
            new InMemoryItemCategoryRepository(AStoredCategory()), roles: "Loader");
        using var client = api.CreateClient();

        var category = await client.GetFromJsonAsync<JsonElement>(
            $"/categories/{CategoryId}", TestContext.Current.CancellationToken);

        category.GetProperty("nameEn").GetString().Should().Be("Medicine");
        category.GetProperty("isFixed").GetBoolean().Should().BeTrue();
        category.GetProperty("isSensitive").GetBoolean().Should().BeTrue();
        category.GetProperty("warnWithinDays").GetInt32().Should().Be(180);
        category.GetProperty("euCode").GetString().Should().Be("300490");
        category.GetProperty("ukCode").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_unknown_category_is_not_found()
    {
        await using var api = FreedomApi.WithCategories(new InMemoryItemCategoryRepository(), roles: "Loader");
        using var client = api.CreateClient();

        (await client.GetAsync("/categories/99", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_administrator_creates_a_category_that_is_never_fixed()
    {
        var categories = new InMemoryItemCategoryRepository(AStoredCategory());
        await using var api = FreedomApi.WithCategories(categories, roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/categories", new { nameEn = "Bedding", isFixed = true }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        categories.Category(3)!.IsFixed.Should().BeFalse();
    }

    [Fact]
    public async Task A_name_already_in_use_is_a_conflict()
    {
        await using var api = FreedomApi.WithCategories(
            new InMemoryItemCategoryRepository(AStoredCategory()), roles: "Administrator");
        using var client = api.CreateClient();

        (await client.PostAsJsonAsync("/categories", ACreateBody("medicine"), TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Updating_a_category_keeps_it_fixed_and_keeps_its_codes()
    {
        var categories = new InMemoryItemCategoryRepository(AStoredCategory());
        await using var api = FreedomApi.WithCategories(categories, roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/categories/{CategoryId}",
            new { nameEn = "Medicines", nameUk = "Ліки", isSensitive = true, warnWithinDays = 120 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var stored = categories.Category(CategoryId)!;
        stored.NameEn.Should().Be("Medicines");
        stored.NameUk.Should().Be("Ліки");
        stored.WarnWithinDays.Should().Be(120);
        stored.IsFixed.Should().BeTrue();
        stored.EuCode.Should().Be("300490");
    }

    [Fact]
    public async Task Updating_an_unknown_category_is_not_found_and_taking_another_categorys_name_is_a_conflict()
    {
        await using var api = FreedomApi.WithCategories(
            new InMemoryItemCategoryRepository(AStoredCategory(), AStoredCategory(3, "Food", isFixed: true)),
            roles: "Administrator");
        using var client = api.CreateClient();

        (await client.PutAsJsonAsync("/categories/99", ACreateBody(), TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync($"/categories/{CategoryId}", ACreateBody("Food"), TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_code_is_set_per_authority_and_cleared_with_null()
    {
        var categories = new InMemoryItemCategoryRepository(AStoredCategory());
        await using var api = FreedomApi.WithCategories(categories, roles: "Administrator");
        using var client = api.CreateClient();

        (await client.PutAsJsonAsync(
            $"/categories/{CategoryId}/codes/uk", new { code = "30049000" }, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        categories.Category(CategoryId)!.UkCode.Should().Be("30049000");

        (await client.PutAsJsonAsync(
            $"/categories/{CategoryId}/codes/EU", new { code = (string?)null }, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        categories.Category(CategoryId)!.EuCode.Should().BeNull();
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("12345678901")]
    [InlineData("30049A")]
    public async Task A_code_that_is_not_six_to_ten_digits_is_rejected(string code)
    {
        await using var api = FreedomApi.WithCategories(
            new InMemoryItemCategoryRepository(AStoredCategory()), roles: "Administrator");
        using var client = api.CreateClient();

        (await client.PutAsJsonAsync(
            $"/categories/{CategoryId}/codes/EU", new { code }, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_authority_that_does_not_exist_is_rejected_and_so_is_a_category_that_does_not()
    {
        await using var api = FreedomApi.WithCategories(
            new InMemoryItemCategoryRepository(AStoredCategory()), roles: "Administrator");
        using var client = api.CreateClient();

        (await client.PutAsJsonAsync(
            $"/categories/{CategoryId}/codes/Mars", new { code = "300490" }, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync(
            "/categories/99/codes/EU", new { code = "300490" }, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public async Task A_hazard_class_outside_one_to_nine_is_rejected(int hazardClass)
    {
        await using var api = FreedomApi.WithCategories(new InMemoryItemCategoryRepository(), roles: "Administrator");
        using var client = api.CreateClient();

        (await client.PostAsJsonAsync(
            "/categories", new { nameEn = "Gas", hazardClass }, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

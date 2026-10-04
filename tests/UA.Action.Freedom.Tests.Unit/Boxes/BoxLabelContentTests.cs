using System.Text.Json;
using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Categories;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Boxes;

/// <summary>
/// What a label may say (D2, the label review): one line per item, naming its category in English and Ukrainian with
/// its quantity and expiry, and who signed. Never the free text, the properties, the value, the donor, the
/// receiver or the location. The type that carries it has nowhere to put any of those.
/// </summary>
public class BoxLabelContentTests
{
    private const int BoxId = 7;

    private static readonly Guid Signer = new("2b9c1e40-7d8a-4c31-9f52-6a0b8d3e5c11");

    private static BoxReadModel ABox(bool validated = true) => new(
        BoxId,
        WeightKg: 24,
        WidthCm: null,
        DepthCm: null,
        HeightCm: null,
        ReceiverRef: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        LocationId: 3,
        ValidatedByPersonId: validated ? Signer : null,
        ValidatedAt: validated ? new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc) : null);

    private static ItemCategoryReadModel ACategory(int id, string nameEn, string nameUk = "") => new(
        id, nameEn, nameUk, IsFixed: true, HazardClass: null, IsSensitive: false, IsNotCarried: false,
        WarnWithinDays: null);

    private static BoxItemReadModel AnItem(
        int categoryId, int? quantity = 3, DateOnly? expiresOn = null, string description = "Blankets") => new(
        Guid.NewGuid(), description, new Dictionary<string, string> { ["note"] = "for the 3rd brigade" }, categoryId,
        Quantity: quantity, ValueGbp: 987.65m, ValueSource: ValueSource.Donor, ExpiresOn: expiresOn, DonationId: 41);

    private static PersonReadModel APerson(string first = "Alex", string last = "Example") => new(
        Signer, first, last, new DateTime(1985, 1, 1), new DateTime(2024, 1, 1), "07700 900123",
        IsDriver: true, Committed: true);

    private static GetBoxLabelContentHandler AHandler(
        BoxReadModel? box,
        IReadOnlyList<BoxItemReadModel> items,
        IReadOnlyList<ItemCategoryReadModel> categories,
        PersonReadModel? signer = null)
    {
        var boxes = Substitute.For<IBoxRepository>();
        boxes.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(box);
        boxes.ListItemsAsync(BoxId, Arg.Any<CancellationToken>()).Returns(items);

        var categoryRepository = Substitute.For<IItemCategoryRepository>();
        categoryRepository.ListAsync(Arg.Any<CancellationToken>()).Returns(categories);

        var people = Substitute.For<IPersonRepository>();
        people.GetByIdAsync(Signer, Arg.Any<CancellationToken>()).Returns(signer);

        return new GetBoxLabelContentHandler(boxes, categoryRepository, people);
    }

    private static Task<BoxLabelContent?> Read(GetBoxLabelContentHandler handler) =>
        handler.HandleAsync(new GetBoxLabelContentQuery(BoxId), CancellationToken.None);

    [Fact]
    public async Task A_box_that_does_not_exist_has_no_label()
    {
        (await Read(AHandler(box: null, [], []))).Should().BeNull();
    }

    [Fact]
    public async Task There_is_one_line_per_item_naming_its_category_in_both_languages()
    {
        var handler = AHandler(
            ABox(),
            [AnItem(1, quantity: 4), AnItem(2, quantity: 10)],
            [ACategory(1, "Clothing", "Одяг"), ACategory(2, "Food", "Їжа")],
            APerson());

        var content = (await Read(handler))!;

        content.Lines.Select(line => (line.CategoryEn, line.CategoryUk, line.Quantity))
            .Should().Equal(("Clothing", "Одяг", 4), ("Food", "Їжа", 10));
    }

    [Fact]
    public async Task A_category_the_administrator_has_not_translated_reads_in_english_on_both_sides()
    {
        // No machine translation is wired (spike, owner decision 4): the Ukrainian is Administrator-edited data.
        var handler = AHandler(ABox(), [AnItem(1)], [ACategory(1, "Clothing", nameUk: "")], APerson());

        var line = (await Read(handler))!.Lines.Single();

        line.CategoryUk.Should().Be("Clothing");
    }

    [Fact]
    public async Task An_item_with_no_quantity_counts_as_one()
    {
        var handler = AHandler(ABox(), [AnItem(1, quantity: null)], [ACategory(1, "Clothing", "Одяг")], APerson());

        (await Read(handler))!.Lines.Single().Quantity.Should().Be(1);
    }

    [Fact]
    public async Task An_items_expiry_date_is_carried_when_it_has_one()
    {
        var expires = new DateOnly(2027, 3, 31);
        var handler = AHandler(
            ABox(), [AnItem(1, expiresOn: expires), AnItem(1, expiresOn: null)], [ACategory(1, "Medicine", "Ліки")],
            APerson());

        (await Read(handler))!.Lines.Select(line => line.ExpiresOn).Should().Equal(null, expires);
    }

    [Fact]
    public async Task Nothing_but_category_quantity_and_expiry_can_reach_the_label()
    {
        // The free-text description, the properties, the value, the donor and the receiver are not on the type, so
        // none of them can be printed, whatever a Loader typed.
        var handler = AHandler(
            ABox(), [AnItem(1, description: "Blankets for Hospital 4, Lviv")], [ACategory(1, "Clothing", "Одяг")],
            APerson());

        var json = JsonSerializer.Serialize(await Read(handler));

        json.Should()
            .NotContain("Blankets").And.NotContain("Hospital").And.NotContain("Lviv").And.NotContain("brigade")
            .And.NotContain("987.65").And.NotContain("11111111-1111").And.NotContain("07700");
    }

    [Fact]
    public async Task The_signer_is_shown_by_a_code_and_a_first_name_with_a_last_initial()
    {
        var handler = AHandler(ABox(), [AnItem(1)], [ACategory(1, "Clothing")], APerson("Alex", "Example"));

        var signer = (await Read(handler))!.Signer!;

        signer.Name.Should().Be("Alex E.");
        signer.Code.Should().MatchRegex("^V-[0-9A-F]{4}-[0-9A-F]{4}$");
        signer.Code.Should().Be(SignerCode.For(Signer), "the same volunteer always prints the same code");
    }

    [Fact]
    public void The_code_does_not_reveal_the_volunteers_identity_and_differs_between_volunteers()
    {
        var other = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

        SignerCode.For(Signer).Should().NotBe(SignerCode.For(other));
        SignerCode.For(Signer).Replace("-", "").Should().NotContain(Signer.ToString("N")[..4].ToUpperInvariant(),
            "it is a one-way hash, not a slice of the identifier");
    }

    [Fact]
    public async Task An_erased_volunteer_reads_as_a_former_volunteer_and_keeps_the_code()
    {
        var handler = AHandler(ABox(), [AnItem(1)], [ACategory(1, "Clothing")], signer: null);

        var signer = (await Read(handler))!.Signer!;

        signer.Name.Should().Be("Former volunteer");
        signer.Code.Should().Be(SignerCode.For(Signer));
    }

    [Fact]
    public async Task A_box_nobody_has_validated_has_no_signer()
    {
        var handler = AHandler(ABox(validated: false), [AnItem(1)], [ACategory(1, "Clothing")]);

        (await Read(handler))!.Signer.Should().BeNull();
    }
}

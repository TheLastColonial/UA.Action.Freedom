using AwesomeAssertions;
using UA.Action.Freedom.Api.Boxes;
using UA.Action.Freedom.Application.Boxes;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// The QR and label renderers directly: what the QR encodes, and what the printable label does
/// and does not say.
/// </summary>
/// <remarks>
/// The renderers are pure so the output is a fixed thing tests can pin. The label's inputs are
/// a box id, a token and a date — it has no parameter through which a receiver or an address
/// could reach it, which is what makes the redaction structural (docs/domain/key-concepts.md
/// § Data Sensitivity).
/// </remarks>
public class BoxQrRendererTests
{
    private static readonly Guid Token = new("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

    [Fact]
    public void The_scan_url_is_the_resolve_endpoint_for_the_token()
    {
        QrCodeRenderer.ScanUrl(Token, "https://freedom.example.org")
            .Should().Be($"https://freedom.example.org/boxes/scan/{Token}");
    }

    [Fact]
    public void The_scan_url_does_not_double_a_slash_from_the_base_url()
    {
        QrCodeRenderer.ScanUrl(Token, "https://freedom.example.org/")
            .Should().Be($"https://freedom.example.org/boxes/scan/{Token}");
    }

    [Fact]
    public void The_qr_svg_is_deterministic()
    {
        var first = QrCodeRenderer.ToSvg(Token, "https://freedom.example.org");
        var second = QrCodeRenderer.ToSvg(Token, "https://freedom.example.org");

        first.Should().Be(second);
        first.Should().StartWith("<svg");
    }

    [Fact]
    public void The_qr_png_is_deterministic_and_carries_the_png_signature()
    {
        var first = QrCodeRenderer.ToPng(Token, "https://freedom.example.org");
        var second = QrCodeRenderer.ToPng(Token, "https://freedom.example.org");

        first.Should().Equal(second);
        first.Take(4).Should().Equal([(byte)0x89, (byte)0x50, (byte)0x4E, (byte)0x47]);
    }

    private static readonly DateTime IssuedAt = new(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

    private const string BaseUrl = "https://freedom.example.org";

    private static readonly BoxLabelContent TwoLines = new(
        [
            new LabelLine("Clothing", "Одяг", 4, ExpiresOn: null),
            new LabelLine("Medicine", "Ліки", 10, new DateOnly(2027, 3, 31)),
        ],
        new LabelSigner("V-1A2B-3C4D", "Alex E."));

    private static string Label(BoxLabelContent content) => BoxLabelRenderer.ToSvg(42, Token, IssuedAt, BaseUrl, content);

    [Fact]
    public void The_label_shows_the_box_number_and_the_charity_and_the_issue_date()
    {
        var svg = Label(BoxLabelContent.None);

        svg.Should().StartWith("<svg");
        svg.Should().Contain("BOX #42").And.Contain("UKRAINIAN ACTION").And.Contain("Issued 2026-08-25");
    }

    [Fact]
    public void The_label_lists_each_item_by_category_in_english_and_ukrainian_with_its_quantity()
    {
        var svg = Label(TwoLines);

        svg.Should().Contain("Contents").And.Contain("Вміст");
        svg.Should().Contain("Clothing").And.Contain("Одяг").And.Contain("×4");
        svg.Should().Contain("Medicine").And.Contain("Ліки").And.Contain("×10");
    }

    [Fact]
    public void The_label_shows_an_expiry_date_only_for_the_item_that_has_one()
    {
        var svg = Label(TwoLines);

        svg.Should().Contain("2027-03-31");
        svg.Split("exp ").Length.Should().Be(2, "only the medicine has a date");
    }

    [Fact]
    public void The_label_names_who_signed_by_code_and_name_in_both_languages()
    {
        var svg = Label(TwoLines);

        svg.Should().Contain("Checked by").And.Contain("Перевірено");
        svg.Should().Contain("V-1A2B-3C4D").And.Contain("Alex E.");
    }

    [Fact]
    public void A_box_nobody_has_validated_says_so_instead_of_naming_a_signer()
    {
        var svg = Label(new BoxLabelContent(TwoLines.Lines, Signer: null));

        svg.Should().Contain("Not validated").And.Contain("Не перевірено");
        svg.Should().NotContain("Checked by");
    }

    [Fact]
    public void The_label_grows_to_fit_the_items()
    {
        var many = new BoxLabelContent(
            [.. Enumerable.Range(0, 20).Select(i => new LabelLine($"Item {i}", $"Річ {i}", 1, null))], Signer: null);

        HeightOf(Label(many)).Should().BeGreaterThan(HeightOf(Label(BoxLabelContent.None)));
    }

    [Fact]
    public void Category_names_are_escaped_so_they_cannot_break_out_of_the_label()
    {
        var svg = Label(new BoxLabelContent([new LabelLine("Tools & <b>kit</b>", "Інструмент", 1, null)], Signer: null));

        svg.Should().Contain("Tools &amp; &lt;b&gt;kit&lt;/b&gt;").And.NotContain("<b>");
    }

    [Fact]
    public void The_label_is_deterministic()
    {
        Label(TwoLines).Should().Be(Label(TwoLines));
    }

    private static int HeightOf(string svg)
    {
        var marker = "height=\"";
        var from = svg.IndexOf(marker, svg.IndexOf("<svg", StringComparison.Ordinal), StringComparison.Ordinal) + marker.Length;

        return int.Parse(svg[from..svg.IndexOf('"', from)]);
    }
}

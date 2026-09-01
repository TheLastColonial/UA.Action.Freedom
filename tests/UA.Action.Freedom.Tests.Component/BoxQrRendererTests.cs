using AwesomeAssertions;
using UA.Action.Freedom.Api.Boxes;

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

    [Fact]
    public void The_label_shows_the_box_number_and_the_charity_and_the_issue_date()
    {
        var svg = BoxLabelRenderer.ToSvg(
            42, Token, new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc), "https://freedom.example.org");

        svg.Should().StartWith("<svg");
        svg.Should().Contain("BOX #42").And.Contain("UKRAINIAN ACTION").And.Contain("Issued 2026-08-25");
    }

    [Fact]
    public void The_label_is_deterministic()
    {
        var issuedAt = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

        BoxLabelRenderer.ToSvg(42, Token, issuedAt, "https://freedom.example.org")
            .Should().Be(BoxLabelRenderer.ToSvg(42, Token, issuedAt, "https://freedom.example.org"));
    }
}

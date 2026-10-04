using System.Globalization;
using System.Security;
using System.Text;
using UA.Action.Freedom.Application.Boxes;

namespace UA.Action.Freedom.Api.Boxes;

/// <summary>
/// Renders the printable label that goes on a physical box: the QR code, the box number, what is inside it and who
/// signed for it, in English and Ukrainian, and nothing that identifies where the box is going.
/// </summary>
/// <remarks>
/// The label crosses borders and may be inspected. Its inputs are a box id, a token, a date and a
/// <see cref="BoxLabelContent"/>: a purpose-built list of category, quantity and expiry lines plus a signer. There
/// is nowhere in this signature to put a receiver, a region, an address, a free-text description, a value or a
/// donor, so the redaction is structural rather than a rule someone has to remember
/// (docs/domain/key-concepts.md § Data Sensitivity, docs/security/0011-label-review.md). It must never take a box or
/// an item read model. Output is a self-contained SVG: scalable for any label size, deterministic, and a PDF with
/// letterhead can wrap it later without changing what is on it. The fixed Ukrainian wording below was written
/// without a translator and is for a Ukrainian speaker to confirm.
/// </remarks>
public static class BoxLabelRenderer
{
    private const int Width = 560;

    private const int HeaderHeight = 240;

    private const int LineHeight = 22;

    private const int ContentsHeadingHeight = 44;

    private const int FooterHeight = 56;

    private const string Font = "Helvetica, Arial, sans-serif";

    public static string ToSvg(int boxId, Guid token, DateTime issuedAt, string baseUrl, BoxLabelContent content)
    {
        var qr = Convert.ToBase64String(QrCodeRenderer.ToPng(token, baseUrl));
        var issued = issuedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var height = HeaderHeight + ContentsHeadingHeight + (content.Lines.Count * LineHeight) + FooterHeight;

        var svg = new StringBuilder();
        svg.AppendLine(CultureInfo.InvariantCulture, $"""<svg xmlns="http://www.w3.org/2000/svg" width="{Width}" height="{height}" viewBox="0 0 {Width} {height}" role="img" aria-label="Label for box {boxId}">""");
        svg.AppendLine(CultureInfo.InvariantCulture, $"""  <rect x="1" y="1" width="{Width - 2}" height="{height - 2}" fill="#ffffff" stroke="#000000" stroke-width="2"/>""");
        svg.AppendLine($"""  <image x="16" y="16" width="208" height="208" href="data:image/png;base64,{qr}"/>""");
        svg.AppendLine($"""  <text x="248" y="52" font-family="{Font}" font-size="22" font-weight="bold" fill="#000000">UKRAINIAN ACTION</text>""");
        svg.AppendLine($"""  <text x="248" y="112" font-family="{Font}" font-size="44" font-weight="bold" fill="#000000">BOX #{boxId}</text>""");
        svg.AppendLine($"""  <text x="248" y="148" font-family="{Font}" font-size="16" fill="#000000">Issued {issued}</text>""");
        svg.AppendLine($"""  <text x="248" y="200" font-family="{Font}" font-size="13" fill="#000000">Scan to open this box in Freedom</text>""");
        svg.AppendLine(CultureInfo.InvariantCulture, $"""  <line x1="16" y1="{HeaderHeight}" x2="{Width - 16}" y2="{HeaderHeight}" stroke="#000000" stroke-width="1"/>""");
        svg.AppendLine(CultureInfo.InvariantCulture, $"""  <text x="16" y="{HeaderHeight + 28}" font-family="{Font}" font-size="16" font-weight="bold" fill="#000000">Contents / Вміст</text>""");

        var y = HeaderHeight + ContentsHeadingHeight + 14;
        foreach (var line in content.Lines)
        {
            svg.AppendLine(CultureInfo.InvariantCulture, $"""  <text x="16" y="{y}" font-family="{Font}" font-size="14" fill="#000000">{ItemText(line)}</text>""");
            y += LineHeight;
        }

        svg.AppendLine(CultureInfo.InvariantCulture, $"""  <text x="16" y="{height - 22}" font-family="{Font}" font-size="14" fill="#000000">{SignerText(content.Signer)}</text>""");
        svg.Append("</svg>");

        return svg.ToString();
    }

    private static string ItemText(LabelLine line)
    {
        var expiry = line.ExpiresOn is { } date
            ? $" (exp {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})"
            : string.Empty;

        return Escape($"{line.CategoryEn} / {line.CategoryUk}  ×{line.Quantity.ToString(CultureInfo.InvariantCulture)}{expiry}");
    }

    private static string SignerText(LabelSigner? signer) => signer is null
        ? "Not validated / Не перевірено"
        : Escape($"Checked by / Перевірено: {signer.Name} ({signer.Code})");

    private static string Escape(string text) => SecurityElement.Escape(text);
}

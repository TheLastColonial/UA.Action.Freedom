using System.Globalization;

namespace UA.Action.Freedom.Api.Boxes;

/// <summary>
/// Renders the printable label that goes on a physical box: the QR code, the box number, and
/// nothing else that identifies where the box is going.
/// </summary>
/// <remarks>
/// The label crosses borders and may be inspected. Its inputs are a box id, a token and a date —
/// there is nowhere in this signature to put a receiver, a region or an address, so the
/// redaction is structural rather than a rule someone has to remember
/// (docs/domain/key-concepts.md § Data Sensitivity). Output is a self-contained SVG: scalable
/// for any label size, deterministic, and a PDF with letterhead can wrap it later without
/// changing what is on it.
/// </remarks>
public static class BoxLabelRenderer
{
    public static string ToSvg(int boxId, Guid token, DateTime issuedAt, string baseUrl)
    {
        var qr = Convert.ToBase64String(QrCodeRenderer.ToPng(token, baseUrl));
        var issued = issuedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="480" height="240" viewBox="0 0 480 240" role="img" aria-label="Label for box {boxId}">
              <rect x="1" y="1" width="478" height="238" fill="#ffffff" stroke="#000000" stroke-width="2"/>
              <image x="16" y="16" width="208" height="208" href="data:image/png;base64,{qr}"/>
              <text x="248" y="52" font-family="Helvetica, Arial, sans-serif" font-size="22" font-weight="bold" fill="#000000">UKRAINIAN ACTION</text>
              <text x="248" y="112" font-family="Helvetica, Arial, sans-serif" font-size="44" font-weight="bold" fill="#000000">BOX #{boxId}</text>
              <text x="248" y="148" font-family="Helvetica, Arial, sans-serif" font-size="16" fill="#000000">Issued {issued}</text>
              <text x="248" y="200" font-family="Helvetica, Arial, sans-serif" font-size="13" fill="#000000">Scan to open this box in Freedom</text>
            </svg>
            """;
    }
}

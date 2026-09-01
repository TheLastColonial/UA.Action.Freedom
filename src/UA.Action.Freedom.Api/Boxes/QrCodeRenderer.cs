using QRCoder;

namespace UA.Action.Freedom.Api.Boxes;

/// <summary>
/// Renders a box's QR code. Pure and deterministic — the same token and base URL always produce
/// the same bytes — so the endpoints and their tests can rely on the output.
/// </summary>
/// <remarks>
/// Uses QRCoder's managed renderers (<see cref="SvgQRCode"/>, <see cref="PngByteQRCode"/>). The
/// <c>System.Drawing</c>-based <c>QRCode</c> type is deliberately not used: it needs a native
/// dependency that is absent from the Linux container the API ships in.
/// </remarks>
public static class QrCodeRenderer
{
    private const int SvgPixelsPerModule = 4;
    private const int PngPixelsPerModule = 20;

    /// <summary>The URL the QR code encodes: the resolve endpoint for <paramref name="token"/>.</summary>
    public static string ScanUrl(Guid token, string baseUrl) =>
        $"{baseUrl.TrimEnd('/')}/boxes/scan/{token}";

    public static string ToSvg(Guid token, string baseUrl)
    {
        using var data = CreateData(token, baseUrl);
        return new SvgQRCode(data).GetGraphic(SvgPixelsPerModule);
    }

    public static byte[] ToPng(Guid token, string baseUrl)
    {
        using var data = CreateData(token, baseUrl);
        return new PngByteQRCode(data).GetGraphic(PngPixelsPerModule);
    }

    private static QRCodeData CreateData(Guid token, string baseUrl)
    {
        using var generator = new QRCodeGenerator();
        return generator.CreateQrCode(ScanUrl(token, baseUrl), QRCodeGenerator.ECCLevel.Q);
    }
}

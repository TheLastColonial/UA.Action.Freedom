namespace UA.Action.Freedom.Api.Configuration;

/// <summary>
/// Settings about the application's own public identity, as opposed to the backing services it
/// talks to.
/// </summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>
    /// The externally reachable base URL a box QR code points back at (for example
    /// <c>https://freedom.example.org</c>). When empty, each request's own scheme and host are
    /// used — right for <c>dotnet run</c>, wrong behind a proxy that rewrites the host, which is
    /// why the local simulation sets it explicitly.
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}

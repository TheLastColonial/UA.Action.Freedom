namespace UA.Action.Freedom.Api.Configuration;

/// <summary>
/// How this instance sits behind whatever is terminating TLS in front of it.
/// </summary>
public sealed class HostingOptions
{
    public const string SectionName = "Hosting";

    /// <summary>
    /// Whether the application should redirect HTTP to HTTPS itself.
    /// <para>
    /// <see langword="false"/> anywhere a proxy already terminates TLS and forwards plain
    /// HTTP — Cloudflare into Container Apps in the target design, Traefik into this
    /// container locally. Leaving it on behind such a proxy produces a redirect loop,
    /// because the redirect target arrives back at the origin as HTTP again.
    /// </para>
    /// </summary>
    public bool UseHttpsRedirection { get; set; } = true;
}

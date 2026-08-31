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

    /// <summary>
    /// Whether the host serves the operator SPA from <c>wwwroot</c> and falls unmatched
    /// routes back to its <c>index.html</c>.
    /// <para>
    /// <see langword="true"/> by default, so the container image — the only build that has a
    /// populated <c>wwwroot</c> — needs no configuration. When <c>wwwroot</c> is absent (every
    /// non-Docker run, including <c>dotnet test</c>) the static middleware is a no-op and the
    /// fallback simply misses, so the API is unaffected either way.
    /// </para>
    /// </summary>
    public bool ServeStaticFrontend { get; set; } = true;
}

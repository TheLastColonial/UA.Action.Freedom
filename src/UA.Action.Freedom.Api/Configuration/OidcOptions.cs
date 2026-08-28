namespace UA.Action.Freedom.Api.Configuration;

/// <summary>
/// The identity provider the application trusts — Microsoft Entra External ID in Azure,
/// Keycloak locally. Both speak OpenID Connect, so only these values differ.
/// </summary>
public sealed class OidcOptions
{
    public const string SectionName = "Oidc";

    /// <summary>
    /// The issuer, as it appears in the <c>iss</c> claim of a token. This is the URL the
    /// <em>browser</em> uses.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>
    /// Where to fetch the discovery document from. Normally left empty so it is derived
    /// from <see cref="Authority"/>.
    /// <para>
    /// It is set explicitly in the local environment because the browser reaches Keycloak
    /// on <c>localhost</c> while this container reaches it on the compose network. Same
    /// server, two URLs: the issuer must stay browser-facing for token validation to
    /// succeed, so only the metadata fetch is redirected.
    /// </para>
    /// </summary>
    public string? MetadataAddress { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>
    /// Only ever <see langword="false"/> locally, where Keycloak is served over plain HTTP.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>The discovery document URL, however it was arrived at.</summary>
    public string? DiscoveryEndpoint =>
        string.IsNullOrWhiteSpace(MetadataAddress)
            ? string.IsNullOrWhiteSpace(Authority)
                ? null
                : $"{Authority.TrimEnd('/')}/.well-known/openid-configuration"
            : MetadataAddress;
}

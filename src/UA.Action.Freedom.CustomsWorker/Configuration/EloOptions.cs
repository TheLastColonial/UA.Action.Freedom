namespace UA.Action.Freedom.CustomsWorker.Configuration;

/// <summary>
/// How the worker reaches the French customs ELO API, and how it authenticates to it.
/// </summary>
/// <remarks>
/// The ELO spec declares no security scheme, so the bearer token is an ordinary header the caller
/// supplies on every call. It is obtained from a customs OAuth2 endpoint by the Resource Owner
/// Password Credentials grant, and §2.2.5.1 of the service contract asks operators to reuse it for
/// its whole lifetime rather than re-authenticating per request.
///
/// <para>
/// There is no <c>BaseUrl</c> default to inherit: the published spec declares no <c>servers:</c>
/// entry, so <c>AddEloClient</c> requires one and the environment must supply it —
/// <c>https://api.douane.gouv.fr/sibrexit/</c> in production,
/// <c>https://api-moa.douane.gouv.fr/sibrexit/</c> for certification. See
/// <c>docs/schemas/edi/onboarding.md</c>.
/// </para>
/// </remarks>
public sealed class EloOptions
{
    public const string SectionName = "Elo";

    /// <summary>The GUN2 endpoint the three envelope operations are exposed on.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The customs OAuth2 token endpoint. Empty locally, where the stub checks nothing.</summary>
    public string? TokenEndpoint { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>The service account the password grant authenticates as.</summary>
    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>
    /// A fixed token to send when no <see cref="TokenEndpoint"/> is configured.
    /// </summary>
    /// <remarks>
    /// This is how the local simulation works: WireMock does not check the credential, and standing
    /// up an OAuth2 server to mint one nobody verifies would add a container and prove nothing. It
    /// is not a production path — with a token endpoint configured this value is ignored.
    /// </remarks>
    public string StaticToken { get; set; } = "local-development-token";

    /// <summary>Whether a real token can be obtained, rather than the static stand-in used locally.</summary>
    public bool AuthenticatesForReal => !string.IsNullOrWhiteSpace(TokenEndpoint);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}

namespace UA.Action.Freedom.CustomsWorker.Elo;

/// <summary>
/// Supplies the bearer token the ELO API expects in its <c>Authorization</c> header.
/// </summary>
/// <remarks>
/// A port because the ELO spec makes the credential a per-call method argument rather than something
/// an <c>HttpClient</c> pipeline can add — unlike the HMRC SDKs, where an OAuth handler would be
/// chained onto the typed client. That means somebody has to fetch and cache a token, and the
/// caching rule (§2.2.5.1: reuse it for its whole lifetime rather than saturating the authentication
/// server) is worth testing without an authentication server.
/// </remarks>
public interface IEloTokenProvider
{
    Task<string> GetAsync(CancellationToken cancellationToken);
}

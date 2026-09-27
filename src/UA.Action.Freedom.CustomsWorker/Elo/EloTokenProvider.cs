using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.CustomsWorker.Configuration;

namespace UA.Action.Freedom.CustomsWorker.Elo;

/// <summary>
/// Obtains an ELO bearer token by the OAuth2 Resource Owner Password Credentials grant, and holds on
/// to it.
/// </summary>
/// <remarks>
/// §2.2.5.1 of the ELO service contract asks operators to reuse a token for its whole lifetime
/// rather than re-authenticating per call, so the caching here is part of the agreement rather than
/// an optimisation. A single worker process draining a queue is the only caller, so one lock around
/// one field is enough; there is no need for a token cache with eviction.
///
/// <para>
/// Unlike the HMRC SDKs, there is nothing to chain onto the typed client: the ELO spec declares no
/// security scheme, so the credential is a method argument on every operation and something has to
/// hand it over. That something is this.
/// </para>
/// </remarks>
public sealed class EloTokenProvider(
    IHttpClientFactory clients,
    IOptions<EloOptions> options,
    ILogger<EloTokenProvider> logger,
    TimeProvider? time = null) : IEloTokenProvider
{
    /// <summary>
    /// The named client the token requests go on.
    /// </summary>
    /// <remarks>
    /// Created per authentication rather than captured once. This class is a singleton — it has to
    /// be, or the cache it exists for would be per resolve — and an <c>HttpClient</c> held for the
    /// life of a singleton keeps the handler chain it was built with, which is the pooling problem
    /// <see cref="IHttpClientFactory"/> exists to avoid. One extra client an hour costs nothing.
    /// </remarks>
    public const string ClientName = "elo-token";

    /// <summary>
    /// Renew this far before the stated expiry.
    /// </summary>
    /// <remarks>
    /// A token used right up to its expiry is one that expires between being read here and being
    /// checked at the far end, which shows up as an intermittent 401 on a request that has already
    /// been counted as attempted. Thirty seconds costs one extra token fetch an hour.
    /// </remarks>
    private static readonly TimeSpan RenewBefore = TimeSpan.FromSeconds(30);

    private readonly EloOptions _elo = options.Value;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _token;
    private DateTimeOffset _renewAt;

    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        // No token endpoint means the local simulation, where WireMock checks nothing. Standing up
        // an OAuth2 server to mint a credential nobody verifies would add a container and prove
        // nothing about the integration.
        if (!_elo.AuthenticatesForReal)
        {
            return _elo.StaticToken;
        }

        if (_token is { } cached && _time.GetUtcNow() < _renewAt)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            // Another caller may have renewed while this one waited.
            if (_token is { } justRenewed && _time.GetUtcNow() < _renewAt)
            {
                return justRenewed;
            }

            var (token, lifetime) = await Authenticate(cancellationToken);

            _token = token;
            _renewAt = _time.GetUtcNow() + lifetime - RenewBefore;

            logger.LogInformation(
                "Authenticated to the French customs authentication server; the token lasts {Lifetime}.",
                lifetime);

            return token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(string Token, TimeSpan Lifetime)> Authenticate(CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = _elo.ClientId ?? string.Empty,
            ["client_secret"] = _elo.ClientSecret ?? string.Empty,
            ["username"] = _elo.Username ?? string.Empty,
            ["password"] = _elo.Password ?? string.Empty,
        });

        using var http = clients.CreateClient(ClientName);
        using var response = await http.PostAsync(_elo.TokenEndpoint, form, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The status and nothing else. A refusal body echoes the client id and sometimes quotes
            // the credential that was wrong, and this exception is logged by the caller.
            throw new InvalidOperationException(
                $"The French customs authentication server answered {(int)response.StatusCode} for "
                + $"{EloOptions.SectionName}:{nameof(EloOptions.ClientId)}. Check the credentials and that "
                + "this environment is authorised for the API_BREXIT_ELO service.");
        }

        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));

        var token = body.RootElement.TryGetProperty("access_token", out var issued)
            ? issued.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "The French customs authentication server answered 200 with no access_token.");
        }

        // A missing expires_in is treated as a short life rather than an endless one: renewing too
        // often is a nuisance, holding a dead token is an outage.
        var lifetime = body.RootElement.TryGetProperty("expires_in", out var expires)
                       && expires.TryGetInt32(out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromMinutes(5);

        return (token, lifetime);
    }
}

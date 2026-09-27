using Microsoft.Extensions.DependencyInjection;

namespace EDI.ELO;

/// <summary>
/// Registers <see cref="IEloClient"/> as a typed <see cref="HttpClient"/> client.
/// </summary>
public static class EloClientServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IEloClient"/> to the container, backed by <see cref="IHttpClientFactory"/>.
    /// </summary>
    /// <remarks>
    /// Unlike the HMRC SDKs, <see cref="EloClientOptions.BaseUrl"/> has no default (the ELO spec
    /// declares no <c>servers:</c> entry), so <paramref name="configure"/> is required, and this
    /// method throws immediately at registration time if it did not set a <see cref="Uri"/>.
    /// <para/>
    /// Authentication and message correlation are the caller's responsibility on every call:
    /// the spec's three operations each require <c>Authorization</c>, <c>messageCode</c>,
    /// <c>functionalId</c>, <c>messageId</c> and <c>correlationId</c> as plain header
    /// parameters (there is no OAuth2 security scheme), so the generated
    /// <see cref="IEloClient"/> methods take them as ordinary arguments — there is no
    /// <c>AddHttpMessageHandler</c> pipeline to chain here.
    /// </remarks>
    public static IHttpClientBuilder AddEloClient(
        this IServiceCollection services,
        Action<EloClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new EloClientOptions();
        configure(options);

        if (options.BaseUrl is null)
        {
            throw new InvalidOperationException(
                $"{nameof(EloClientOptions)}.{nameof(EloClientOptions.BaseUrl)} must be set. " +
                "The ELO OpenAPI spec declares no servers: entry, so unlike the HMRC SDKs " +
                "there is no default host to fall back to.");
        }

        return services
            .AddHttpClient<IEloClient, EloClient>(http =>
                http.DefaultRequestHeaders.Accept.ParseAdd("application/json"))
            .AddTypedClient<IEloClient>(http => new EloClient(options.BaseUrl.ToString(), http));
    }
}

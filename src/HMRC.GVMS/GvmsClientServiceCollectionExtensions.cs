using Microsoft.Extensions.DependencyInjection;

namespace HMRC.GVMS;

/// <summary>
/// Registers <see cref="IGvmsClient"/> as a typed <see cref="HttpClient"/> client.
/// </summary>
public static class GvmsClientServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IGvmsClient"/> to the container, backed by <see cref="IHttpClientFactory"/>.
    /// The client targets <see cref="GvmsClientOptions.ProductionBaseUrl"/> and sends the
    /// <see cref="GvmsClientOptions.HmrcJsonMediaType"/> <c>Accept</c> header by default.
    /// </summary>
    /// <remarks>
    /// Authentication is the caller's responsibility: chain
    /// <see cref="Microsoft.Extensions.DependencyInjection.HttpClientBuilderExtensions.AddHttpMessageHandler(IHttpClientBuilder)"/>
    /// on the returned builder to attach a handler that adds the OAuth 2.0 bearer token
    /// required by the movement operations.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration callback (e.g. switch to the sandbox base URL).</param>
    /// <returns>The <see cref="IHttpClientBuilder"/> for the underlying named client, for further chaining.</returns>
    public static IHttpClientBuilder AddGvmsClient(
        this IServiceCollection services,
        Action<GvmsClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new GvmsClientOptions();
        configure?.Invoke(options);

        return services
            .AddHttpClient<IGvmsClient, GvmsClient>(http =>
                http.DefaultRequestHeaders.Accept.ParseAdd(GvmsClientOptions.HmrcJsonMediaType))
            .AddTypedClient<IGvmsClient>(http => new GvmsClient(http) { BaseUrl = options.BaseUrl.ToString() });
    }
}

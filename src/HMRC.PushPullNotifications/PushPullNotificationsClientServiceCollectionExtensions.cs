using Microsoft.Extensions.DependencyInjection;

namespace HMRC.PushPullNotifications;

/// <summary>
/// Registers <see cref="IPushPullNotificationsClient"/> as a typed <see cref="HttpClient"/> client.
/// </summary>
public static class PushPullNotificationsClientServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IPushPullNotificationsClient"/> to the container, backed by
    /// <see cref="IHttpClientFactory"/>. The client targets
    /// <see cref="PushPullNotificationsClientOptions.ProductionBaseUrl"/> and sends the
    /// <see cref="PushPullNotificationsClientOptions.HmrcJsonMediaType"/> <c>Accept</c> header
    /// by default.
    /// </summary>
    /// <remarks>
    /// Authentication is the caller's responsibility: chain
    /// <see cref="Microsoft.Extensions.DependencyInjection.HttpClientBuilderExtensions.AddHttpMessageHandler(IHttpClientBuilder)"/>
    /// on the returned builder to attach a handler that adds the OAuth 2.0 bearer token
    /// (client-credentials grant, scope
    /// <see cref="PushPullNotificationsClientOptions.ReadScope"/> or
    /// <see cref="PushPullNotificationsClientOptions.WriteScope"/>) required by the operations.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration callback (e.g. switch to the sandbox base URL).</param>
    /// <returns>The <see cref="IHttpClientBuilder"/> for the underlying named client, for further chaining.</returns>
    public static IHttpClientBuilder AddPushPullNotificationsClient(
        this IServiceCollection services,
        Action<PushPullNotificationsClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new PushPullNotificationsClientOptions();
        configure?.Invoke(options);

        return services
            .AddHttpClient<IPushPullNotificationsClient, PushPullNotificationsClient>(http =>
                http.DefaultRequestHeaders.Accept.ParseAdd(PushPullNotificationsClientOptions.HmrcJsonMediaType))
            .AddTypedClient<IPushPullNotificationsClient>(http =>
                new PushPullNotificationsClient(http) { BaseUrl = options.BaseUrl.ToString() });
    }
}

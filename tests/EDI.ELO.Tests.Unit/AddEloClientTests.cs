using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EDI.ELO.Tests.Unit;

public class AddEloClientTests
{
    private const string TestBaseUrl = "https://elo.example.test/";

    [Fact]
    public void Throws_when_the_configured_base_url_is_not_set()
    {
        var services = new ServiceCollection();

        var act = () => services.AddEloClient(_ => { });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(EloClientOptions.BaseUrl)}*");
    }

    [Fact]
    public void Registers_a_resolvable_typed_client()
    {
        using var provider = new ServiceCollection()
            .AddEloClient(o => o.BaseUrl = new Uri(TestBaseUrl))
            .Services
            .BuildServiceProvider();

        provider.GetService<IEloClient>().Should().BeOfType<EloClient>();
    }

    [Fact]
    public void Applies_the_configured_base_url()
    {
        using var provider = new ServiceCollection()
            .AddEloClient(o => o.BaseUrl = new Uri(TestBaseUrl))
            .Services
            .BuildServiceProvider();

        var client = (EloClient)provider.GetRequiredService<IEloClient>();

        client.BaseUrl.Should().Be(TestBaseUrl);
    }

    [Fact]
    public async Task Sends_requests_to_the_configured_host_with_a_json_accept_header()
    {
        var handler = new CapturingHandler();
        using var provider = new ServiceCollection()
            .AddEloClient(o => o.BaseUrl = new Uri(TestBaseUrl))
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .Services
            .BuildServiceProvider();

        var client = provider.GetRequiredService<IEloClient>();

        await client.CreerENVAsync(
            authorization: "Bearer token",
            messageCode: "ENV_CRE01",
            functionalId: "func-1",
            messageId: "msg-1",
            correlationId: "corr-1",
            body: new ENV_CRE01(),
            cancellationToken: TestContext.Current.CancellationToken);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri.Should().Be(TestBaseUrl + "enveloppe");
        handler.LastRequest.Headers.Accept.Should().ContainSingle()
            .Which.ToString().Should().Be("application/json");
    }

    [Fact]
    public async Task Sends_the_caller_supplied_correlation_headers_as_real_http_headers()
    {
        var handler = new CapturingHandler();
        using var provider = new ServiceCollection()
            .AddEloClient(o => o.BaseUrl = new Uri(TestBaseUrl))
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .Services
            .BuildServiceProvider();

        var client = provider.GetRequiredService<IEloClient>();

        await client.CreerENVAsync(
            authorization: "Bearer abc123",
            messageCode: "ENV_CRE01",
            functionalId: "func-1",
            messageId: "msg-1",
            correlationId: "corr-1",
            body: new ENV_CRE01(),
            cancellationToken: TestContext.Current.CancellationToken);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.GetValues("Authorization").Should().ContainSingle("Bearer abc123");
        handler.LastRequest.Headers.GetValues("messageCode").Should().ContainSingle("ENV_CRE01");
        handler.LastRequest.Headers.GetValues("functionalId").Should().ContainSingle("func-1");
        handler.LastRequest.Headers.GetValues("messageId").Should().ContainSingle("msg-1");
        handler.LastRequest.Headers.GetValues("correlationId").Should().ContainSingle("corr-1");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}

using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using HMRC.GVMS;

namespace HMRC.GVMS.Tests.Unit;

public class AddGvmsClientTests
{
    [Fact]
    public void Registers_a_resolvable_typed_client()
    {
        using var provider = new ServiceCollection().AddGvmsClient().Services.BuildServiceProvider();

        provider.GetService<IGvmsClient>().Should().BeOfType<GvmsClient>();
    }

    [Fact]
    public void Defaults_the_base_url_to_the_hmrc_production_host()
    {
        using var provider = new ServiceCollection().AddGvmsClient().Services.BuildServiceProvider();

        var client = (GvmsClient)provider.GetRequiredService<IGvmsClient>();

        client.BaseUrl.Should().Be(GvmsClientOptions.ProductionBaseUrl);
    }

    [Fact]
    public void Applies_a_configured_base_url()
    {
        using var provider = new ServiceCollection()
            .AddGvmsClient(o => o.BaseUrl = new Uri(GvmsClientOptions.SandboxBaseUrl))
            .Services
            .BuildServiceProvider();

        var client = (GvmsClient)provider.GetRequiredService<IGvmsClient>();

        client.BaseUrl.Should().Be(GvmsClientOptions.SandboxBaseUrl);
    }

    [Fact]
    public async Task Sends_requests_to_the_configured_host_with_the_hmrc_versioned_accept_header()
    {
        var handler = new CapturingHandler();
        using var provider = new ServiceCollection()
            .AddGvmsClient()
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .Services
            .BuildServiceProvider();

        var client = provider.GetRequiredService<IGvmsClient>();

        await client.DeleteGoodsMovementRecordAsync("GMRA000002JR", TestContext.Current.CancellationToken);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri.Should().Be(
            "https://api.service.hmrc.gov.uk/customs/goods-movement-system/movements/GMRA000002JR");
        handler.LastRequest.Headers.Accept.Should().ContainSingle()
            .Which.ToString().Should().Be(GvmsClientOptions.HmrcJsonMediaType);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        }
    }
}

using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using UA.Action.Freedom.Hmrc.PushPullNotifications;

namespace UA.Action.Freedom.Tests.Unit;

public class AddPushPullNotificationsClientTests
{
    [Fact]
    public void Registers_a_resolvable_typed_client()
    {
        using var provider = new ServiceCollection().AddPushPullNotificationsClient().Services.BuildServiceProvider();

        provider.GetService<IPushPullNotificationsClient>().Should().BeOfType<PushPullNotificationsClient>();
    }

    [Fact]
    public void Defaults_the_base_url_to_the_hmrc_production_host()
    {
        using var provider = new ServiceCollection().AddPushPullNotificationsClient().Services.BuildServiceProvider();

        var client = (PushPullNotificationsClient)provider.GetRequiredService<IPushPullNotificationsClient>();

        client.BaseUrl.Should().Be(PushPullNotificationsClientOptions.ProductionBaseUrl);
    }

    [Fact]
    public void Applies_a_configured_base_url()
    {
        using var provider = new ServiceCollection()
            .AddPushPullNotificationsClient(o => o.BaseUrl = new Uri(PushPullNotificationsClientOptions.SandboxBaseUrl))
            .Services
            .BuildServiceProvider();

        var client = (PushPullNotificationsClient)provider.GetRequiredService<IPushPullNotificationsClient>();

        client.BaseUrl.Should().Be(PushPullNotificationsClientOptions.SandboxBaseUrl);
    }

    [Fact]
    public async Task Sends_requests_to_the_configured_host_with_the_hmrc_versioned_accept_header()
    {
        var handler = new CapturingHandler();
        using var provider = new ServiceCollection()
            .AddPushPullNotificationsClient()
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .Services
            .BuildServiceProvider();

        var client = provider.GetRequiredService<IPushPullNotificationsClient>();

        await client.AcknowledgeNotificationsAsync(
            "50dca3fc-c37c-4f03-b719-63571333624c",
            new AcknowledgeNotificationsRequest { NotificationIds = { "1ed5f407-8096-40d1-87ef-9a2a103eeb85" } },
            TestContext.Current.CancellationToken);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri.Should().Be(
            "https://api.service.hmrc.gov.uk/misc/push-pull-notification/box/50dca3fc-c37c-4f03-b719-63571333624c/notifications/acknowledge");
        handler.LastRequest.Headers.Accept.Should().ContainSingle()
            .Which.ToString().Should().Be(PushPullNotificationsClientOptions.HmrcJsonMediaType);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}

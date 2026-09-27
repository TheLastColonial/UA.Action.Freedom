using System.Net;
using System.Text;
using AwesomeAssertions;
using MELT;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.CustomsWorker.Configuration;
using UA.Action.Freedom.CustomsWorker.Elo;
using UA.Action.Freedom.Tests.Unit.Telemetry;

namespace UA.Action.Freedom.Tests.Unit.CustomsWorker;

/// <summary>
/// Getting the bearer token the ELO API wants on every call.
/// </summary>
/// <remarks>
/// §2.2.5.1 of the ELO service contract is explicit: authenticate by the Resource Owner Password
/// Credentials grant, then <em>reuse the token for its entire lifetime so as not to unnecessarily
/// saturate the authentication server</em>. A provider that fetches per request would be functionally
/// correct and in breach of the operator agreement, so the caching is the behaviour under test rather
/// than an optimisation.
/// </remarks>
public sealed class EloTokenProviderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly FixedTime _time = new(Now);
    private readonly CountingHandler _handler = new();

    public void Dispose() => _handler.Dispose();

    [Fact]
    public async Task Authenticates_with_the_password_grant_the_service_contract_specifies()
    {
        var token = await Provider().GetAsync(TestContext.Current.CancellationToken);

        token.Should().Be("issued-token");
        _handler.LastRequestUri.Should().Be("https://customs.example.test/oauth2/token");

        var form = _handler.LastBody!;
        form.Should().Contain("grant_type=password");
        form.Should().Contain("client_id=freedom");
        form.Should().Contain("username=service-account");
    }

    [Fact]
    public async Task Reuses_a_token_for_its_lifetime_rather_than_asking_again()
    {
        var provider = Provider();

        await provider.GetAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(30));
        var second = await provider.GetAsync(TestContext.Current.CancellationToken);

        second.Should().Be("issued-token");
        _handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Asks_again_once_the_token_has_expired()
    {
        var provider = Provider();

        await provider.GetAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromHours(2));
        await provider.GetAsync(TestContext.Current.CancellationToken);

        _handler.Calls.Should().Be(2);
    }

    /// <summary>
    /// A token reused right up to its stated expiry is one that expires in flight, so the provider
    /// renews slightly early. The margin is what stops a valid-looking token producing a 401 on a
    /// call that has already been counted as attempted.
    /// </summary>
    [Fact]
    public async Task Renews_a_little_before_expiry_rather_than_exactly_at_it()
    {
        var provider = Provider();

        await provider.GetAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(3570));
        await provider.GetAsync(TestContext.Current.CancellationToken);

        _handler.Calls.Should().Be(2);
    }

    /// <summary>
    /// The local simulation stubs the ELO API with WireMock, which does not check the credential.
    /// Standing up an OAuth2 server to mint a token nobody verifies would add a container and prove
    /// nothing, so with no token endpoint configured the provider returns the configured stand-in and
    /// makes no request at all.
    /// </summary>
    [Fact]
    public async Task Uses_the_configured_stand_in_when_there_is_no_token_endpoint()
    {
        var provider = Provider(new EloOptions
        {
            BaseUrl = "http://wiremock:8080/",
            StaticToken = "local-development-token",
        });

        var token = await provider.GetAsync(TestContext.Current.CancellationToken);

        token.Should().Be("local-development-token");
        _handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Says_what_is_missing_when_the_authentication_server_refuses()
    {
        _handler.Status = HttpStatusCode.Unauthorized;
        _handler.Body = """{ "error": "invalid_client" }""";
        var logs = TestLoggerFactory.Create();

        var act = () => Provider(logs: logs).GetAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*401*");
    }

    /// <summary>
    /// The credential is in the request and often echoed in the refusal body. Neither belongs in a
    /// log that is retained.
    /// </summary>
    [Fact]
    public async Task Never_logs_the_credential_it_authenticated_with()
    {
        _handler.Status = HttpStatusCode.Unauthorized;
        _handler.Body = """{ "error": "invalid_client", "hint": "secret-shibboleth is wrong" }""";
        var logs = TestLoggerFactory.Create();

        try
        {
            await Provider(logs: logs).GetAsync(TestContext.Current.CancellationToken);
        }
        catch (InvalidOperationException)
        {
            // The refusal itself is asserted above; this test is about what was written down.
        }

        var written = string.Join(
            " ", logs.Sink.LogEntries.Select(entry => $"{entry.Message} {entry.Exception}"));
        written.Should().NotContain("secret-shibboleth").And.NotContain("service-account");
    }

    private EloTokenProvider Provider(EloOptions? options = null, ITestLoggerFactory? logs = null) => new(
        new StubClientFactory(_handler),
        Options.Create(options ?? new EloOptions
        {
            BaseUrl = "https://customs.example.test/sibrexit/",
            TokenEndpoint = "https://customs.example.test/oauth2/token",
            ClientId = "freedom",
            ClientSecret = "secret-shibboleth",
            Username = "service-account",
            Password = "service-password",
        }),
        (logs ?? TestLoggerFactory.Create()).CreateLogger<EloTokenProvider>(),
        _time);

    /// <summary>
    /// Hands out clients over one shared handler, so the count is of requests rather than of clients
    /// — the provider creates a client per authentication on purpose.
    /// </summary>
    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public string? LastRequestUri { get; private set; }

        public string? LastBody { get; private set; }

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string Body { get; set; } = """{ "access_token": "issued-token", "expires_in": 3600 }""";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequestUri = request.RequestUri?.ToString();
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(Body, Encoding.UTF8, "application/json"),
            };
        }
    }
}

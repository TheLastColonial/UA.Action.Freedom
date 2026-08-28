using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Stands in for the JWT bearer scheme in component tests: no real token, a principal whose
/// <c>roles</c> claims and authenticated state the test dictates. This is what lets the
/// tests exercise the authorization policies without a Keycloak.
/// </summary>
internal sealed class TestAuthOptions : AuthenticationSchemeOptions
{
    public string[] Roles { get; set; } = [];

    public bool Authenticated { get; set; } = true;
}

internal sealed class TestAuthHandler(
    IOptionsMonitor<TestAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<TestAuthOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    private const string RoleClaimType = "roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Options.Authenticated)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "test-user") };
        claims.AddRange(Options.Roles.Select(role => new Claim(RoleClaimType, role)));

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.NameIdentifier, RoleClaimType);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

namespace UA.Action.Freedom.Tests.BDD.Support;

/// <summary>
/// Where the BDD scenarios point. Defaults match the local <c>iac</c> stack (edge on 8080,
/// Keycloak on 8081); every value is overridable by environment variable so the same suite
/// can run against another deployed environment.
/// </summary>
internal static class FreedomTarget
{
    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("FREEDOM_BASE_URL") ?? "http://localhost:8080";

    public static string OidcTokenEndpoint =>
        (Environment.GetEnvironmentVariable("FREEDOM_OIDC_URL") ?? "http://localhost:8081/realms/freedom")
            .TrimEnd('/') + "/protocol/openid-connect/token";

    public static string OidcClientId =>
        Environment.GetEnvironmentVariable("FREEDOM_OIDC_CLIENT_ID") ?? "freedom-app";

    public static string OidcClientSecret =>
        Environment.GetEnvironmentVariable("FREEDOM_OIDC_CLIENT_SECRET") ?? "local-freedom-client-secret";

    public static string TestUserPassword =>
        Environment.GetEnvironmentVariable("FREEDOM_TEST_PASSWORD") ?? "password";
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Builds the Freedom Application in memory with an explicit configuration, so each test
/// states the environment it is describing rather than inheriting one.
/// </summary>
internal static class FreedomApi
{
    /// <summary>
    /// The application as it runs with nothing behind it — no database, no storage account,
    /// no identity provider. This is the state a developer hits before
    /// <c>docker compose up</c>, and the application is expected to start anyway.
    /// </summary>
    internal static WebApplicationFactory<Program> WithNoBackingServices() =>
        With(new Dictionary<string, string?>());

    /// <summary>
    /// The application pointed at backing services that are configured but unreachable —
    /// the shape of a misconfiguration, or of Azure SQL still waking from auto-pause.
    /// </summary>
    internal static WebApplicationFactory<Program> WithUnreachableBackingServices() =>
        With(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Freedom"] =
                "Server=127.0.0.1,14330;Database=Freedom;User Id=sa;Password=nope;TrustServerCertificate=True;Connect Timeout=1",
            ["Storage:ConnectionString"] =
                "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10990/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10991/devstoreaccount1;",
            ["Oidc:MetadataAddress"] = "http://127.0.0.1:10992/realms/freedom/.well-known/openid-configuration",
        });

    internal static WebApplicationFactory<Program> With(IDictionary<string, string?> settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Hosting:UseHttpsRedirection", "false");

            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });
}

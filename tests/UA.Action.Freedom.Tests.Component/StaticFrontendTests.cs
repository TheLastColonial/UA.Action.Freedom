using System.Net;
using AwesomeAssertions;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// The operator UI is a React SPA baked into <c>wwwroot/app</c> at image-build time and
/// served same-origin by this host under <c>/app</c>. These tests pin the two things that
/// must stay true: serving the SPA never shadows an API route or a health probe (it is
/// scoped to <c>/app</c>), and a host with no built SPA (every non-Docker run, including
/// <c>dotnet test</c>) still serves the API unchanged.
/// </summary>
public class StaticFrontendTests
{
    private const string IndexHtml = "<!doctype html><title>freedom-spa-marker</title>";

    [Fact]
    public async Task The_app_root_is_not_found_when_no_frontend_has_been_built()
    {
        using var webRoot = new TemporaryDirectory();
        await using var api = FreedomApi.WithWebRoot(webRoot.Path);
        using var client = api.CreateClient();

        var appRoot = await client.GetAsync("/app/", TestContext.Current.CancellationToken);

        appRoot.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_api_and_health_probes_are_unaffected_when_no_frontend_has_been_built()
    {
        using var webRoot = new TemporaryDirectory();
        await using var api = FreedomApi.WithWebRoot(webRoot.Path);
        using var client = api.CreateClient();

        var live = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        var vehicles = await client.GetAsync("/vehicles", TestContext.Current.CancellationToken);

        live.StatusCode.Should().Be(HttpStatusCode.OK);
        vehicles.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_frontend_index_is_served_at_the_app_root()
    {
        using var webRoot = new TemporaryDirectory();
        await webRoot.WriteAppIndexHtml(IndexHtml);
        await using var api = FreedomApi.WithWebRoot(webRoot.Path);
        using var client = api.CreateClient();

        var appRoot = await client.GetAsync("/app/", TestContext.Current.CancellationToken);
        var body = await appRoot.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        appRoot.StatusCode.Should().Be(HttpStatusCode.OK);
        appRoot.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        body.Should().Contain("freedom-spa-marker");
    }

    [Fact]
    public async Task An_unmatched_client_route_falls_back_to_the_frontend_index()
    {
        using var webRoot = new TemporaryDirectory();
        await webRoot.WriteAppIndexHtml(IndexHtml);
        await using var api = FreedomApi.WithWebRoot(webRoot.Path);
        using var client = api.CreateClient();

        var deepLink = await client.GetAsync("/app/manifests/ABC-123", TestContext.Current.CancellationToken);
        var body = await deepLink.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        deepLink.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("freedom-spa-marker");
    }

    [Fact]
    public async Task A_built_asset_is_served_as_a_file_not_the_index_fallback()
    {
        using var webRoot = new TemporaryDirectory();
        await webRoot.WriteAppIndexHtml(IndexHtml);
        await webRoot.WriteAppAsset("assets/app-abc123.js", "export const marker = 'asset';");
        await using var api = FreedomApi.WithWebRoot(webRoot.Path);
        using var client = api.CreateClient();

        var asset = await client.GetAsync("/app/assets/app-abc123.js", TestContext.Current.CancellationToken);
        var body = await asset.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        asset.StatusCode.Should().Be(HttpStatusCode.OK);
        asset.Content.Headers.ContentType?.MediaType.Should().Be("text/javascript");
        body.Should().Contain("marker");
    }

    [Fact]
    public async Task The_frontend_fallback_does_not_shadow_the_api_or_health_probes()
    {
        using var webRoot = new TemporaryDirectory();
        await webRoot.WriteAppIndexHtml(IndexHtml);
        await using var api = FreedomApi.WithWebRoot(webRoot.Path);
        using var client = api.CreateClient();

        var ready = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        var vehicles = await client.GetAsync("/vehicles", TestContext.Current.CancellationToken);

        ready.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        vehicles.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_frontend_is_not_served_when_static_hosting_is_disabled()
    {
        using var webRoot = new TemporaryDirectory();
        await webRoot.WriteAppIndexHtml(IndexHtml);
        await using var api = FreedomApi.WithWebRoot(webRoot.Path, serveStaticFrontend: false);
        using var client = api.CreateClient();

        var appRoot = await client.GetAsync("/app/", TestContext.Current.CancellationToken);

        appRoot.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("freedom-web-").FullName;

        public string Path { get; }

        public async Task WriteAppIndexHtml(string html)
        {
            var appDirectory = System.IO.Path.Combine(Path, "app");
            Directory.CreateDirectory(appDirectory);
            await File.WriteAllTextAsync(System.IO.Path.Combine(appDirectory, "index.html"), html);
        }

        public async Task WriteAppAsset(string relativePath, string content)
        {
            var target = System.IO.Path.Combine(Path, "app", relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, content);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}

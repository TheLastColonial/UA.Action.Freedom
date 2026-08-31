using System.Net;
using System.Text.Json;
using AwesomeAssertions;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// The health contract the local environment and Azure Container Apps both rely on.
/// Liveness answers "is this process worth keeping"; readiness answers "can it serve
/// traffic yet". Conflating them is what makes a container restart-loop while its
/// database is still waking from auto-pause.
/// </summary>
public class HealthEndpointTests
{
    [Fact]
    public async Task Liveness_is_healthy_even_when_no_backing_service_exists()
    {
        await using var api = FreedomApi.WithNoBackingServices();
        using var client = api.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Liveness_does_not_wait_on_a_database_that_is_waking_up()
    {
        await using var api = FreedomApi.WithUnreachableBackingServices();
        using var client = api.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_refuses_traffic_when_the_backing_services_are_unreachable()
    {
        await using var api = FreedomApi.WithUnreachableBackingServices();
        using var client = api.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Readiness_names_every_dependency_it_checked_and_how_each_fared()
    {
        await using var api = FreedomApi.WithUnreachableBackingServices();
        using var client = api.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        var report = await ReadReport(response);

        report.Should().ContainKeys("database", "documents", "customs-queue", "identity");
        report.Values.Should().AllBe("Unhealthy");
    }

    [Fact]
    public async Task Readiness_reports_an_unconfigured_dependency_as_unhealthy_rather_than_skipping_it()
    {
        await using var api = FreedomApi.WithNoBackingServices();
        using var client = api.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        var report = await ReadReport(response);

        report.Should().ContainKeys("database", "documents", "customs-queue", "identity");
        report.Values.Should().AllBe("Unhealthy");
    }

    [Fact]
    public async Task Readiness_gives_up_on_an_unreachable_dependency_quickly_enough_to_be_probed()
    {
        // Docker's healthcheck timeout and Traefik's load-balancer health check are both
        // measured in single-digit seconds. An SDK left on its default retry policy takes
        // minutes to admit defeat, which turns a readiness probe into a probe that only
        // ever reports success or times out — the one answer it must never give.
        await using var api = FreedomApi.WithUnreachableBackingServices();
        using var client = api.CreateClient();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task The_application_starts_promptly_even_when_the_storage_account_is_unreachable()
    {
        // Persisting the data-protection key ring to blob storage (recommendations 3.2)
        // means the key ring is read during startup. On an untuned client that read spends
        // the SDK's full retry budget before giving up, so an unreachable storage account
        // turns every cold start into a minutes-long stall — and with minReplicas: 0, cold
        // starts are the normal case. The application must come up and report the problem
        // on /health/ready instead of hanging on the way in.
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await using var api = FreedomApi.WithUnreachableBackingServices();
        using var client = api.CreateClient();
        await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task The_application_still_starts_when_no_storage_account_is_configured()
    {
        await using var api = FreedomApi.WithNoBackingServices();
        using var client = api.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<Dictionary<string, string>> ReadReport(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return document.RootElement
            .GetProperty("checks")
            .EnumerateArray()
            .ToDictionary(
                check => check.GetProperty("name").GetString()!,
                check => check.GetProperty("status").GetString()!);
    }
}

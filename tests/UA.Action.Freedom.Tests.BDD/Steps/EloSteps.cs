using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Reqnroll;
using UA.Action.Freedom.Tests.BDD.Support;

namespace UA.Action.Freedom.Tests.BDD.Steps;

/// <summary>
/// Steps for the French logistics envelope, and the one join no other feature covers: putting a
/// validated box on a manifest.
/// </summary>
/// <remarks>
/// Reqnroll matches step text globally, so only genuinely new phrasings belong here — a duplicate
/// of something in <see cref="ApiSteps"/>, <see cref="BoxesSteps"/> or <see cref="ManifestsSteps"/>
/// is an ambiguous binding rather than an override.
/// </remarks>
[Binding]
public sealed class EloSteps(FreedomApiClient api, ScenarioState state)
{
    private const string ManifestKey = "manifest";

    /// <summary>
    /// How often to ask while waiting for the Customs Worker. Short enough that the scenario is not
    /// mostly sleeping, long enough not to hammer the edge for a minute.
    /// </summary>
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Waits for the envelope rather than asserting it immediately.
    /// </summary>
    /// <remarks>
    /// Approving a manifest puts an envelope request on a queue; the Customs Worker drains that queue
    /// on a poll loop, calls French customs and writes the result to blob storage. None of that has
    /// happened when <c>approve</c> returns, and it must not — a synchronous call to a foreign
    /// customs authority inside an HTTP request is the thing the durable hand-off exists to avoid.
    /// <para>
    /// A fixed sleep would be flaky in CI and slow locally, so this polls to a budget and fails with
    /// what the last attempt actually said.
    /// </para>
    /// </remarks>
    [Then("within (\\d+) seconds the remembered manifest has a French logistics envelope")]
    public async Task ThenWithinSecondsTheManifestHasAnEnvelope(int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        HttpStatusCode last;

        do
        {
            var response = await api.SendAsync(HttpMethod.Get, EnvelopePath(), state.CurrentToken, null);
            last = response.StatusCode;

            if (last == HttpStatusCode.OK)
            {
                return;
            }

            if (last != HttpStatusCode.NotFound)
            {
                // A 401/403/500 will not become a 200 by waiting, and reporting it as a timeout
                // would send whoever reads the failure to look at the worker instead of the API.
                break;
            }

            await Task.Delay(PollEvery);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Fail(
            $"No French logistics envelope appeared for manifest {state.Pinned(ManifestKey)} within "
            + $"{seconds}s; the last GET {EnvelopePath()} answered {(int)last}. Is the customs-worker "
            + $"container running, and did `tofu apply` create the elo-envelopes queue? The body was: "
            + api.LastBody);
    }

    [Then("the envelope names a declaration and is closed but not yet paired")]
    public void ThenTheEnvelopeIsClosedButNotPaired()
    {
        var envelope = JsonDocument.Parse(api.LastBody).RootElement;

        // FERMEE is where a newly created envelope sits: closed, and not yet paired to a physical
        // crossing. APPAIREE/EMBARQUEE/DEBARQUEE happen at the port, and Freedom does not follow
        // them yet (docs/gotchas-and-open-questions.md 8).
        envelope.GetProperty("statut").GetString().Should().Be("FERMEE", "the body was: {0}", api.LastBody);
        envelope.GetProperty("numeroDossier").GetString().Should().NotBeNullOrWhiteSpace();
        envelope.GetProperty("declarationCount").GetInt32().Should()
            .BeGreaterThan(0, "a loaded lorry's envelope must name at least one formality (ENV_CTR_RG08)");
    }

    /// <summary>
    /// The barcode is the artifact a driver actually presents, and the spec models its field in a way
    /// that generated an unusable type until the codegen was corrected — so it is worth proving that
    /// what comes out of the far end is a PDF rather than that a field is non-empty.
    /// </summary>
    [Then("the envelope's barcode document is a PDF")]
    public async Task ThenTheBarcodeIsAPdf()
    {
        var response = await api.SendAsync(
            HttpMethod.Get, EnvelopePath("/document"), state.CurrentToken, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the body was: {0}", api.LastBody);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().HaveCountGreaterThan(4);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    /// <summary>
    /// Delivery does not discard the paperwork. The envelope records that this vehicle was cleared to
    /// cross, which stays true after it has arrived.
    /// </summary>
    [Then("the remembered manifest still has its French logistics envelope")]
    public async Task ThenTheManifestStillHasItsEnvelope()
    {
        var response = await api.SendAsync(HttpMethod.Get, EnvelopePath(), state.CurrentToken, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the body was: {0}", api.LastBody);
    }

    private string EnvelopePath(string suffix = "") =>
        $"/manifests/{state.Pinned(ManifestKey)}/elo{suffix}";
}

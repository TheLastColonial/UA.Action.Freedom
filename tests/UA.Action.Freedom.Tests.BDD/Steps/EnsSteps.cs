using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Reqnroll;
using UA.Action.Freedom.Tests.BDD.Support;

namespace UA.Action.Freedom.Tests.BDD.Steps;

/// <summary>
/// Steps for the ICS2 Entry Summary Declaration, which is the one thing approval will not proceed
/// without.
/// </summary>
/// <remarks>
/// Reqnroll matches step text globally, so only genuinely new phrasings belong here — a duplicate of
/// something in <see cref="ApiSteps"/>, <see cref="ManifestsSteps"/> or <see cref="EloSteps"/> is an
/// ambiguous binding rather than an override.
///
/// <para>
/// Nothing here calls ICS2, because Freedom does not: the Shared Trader Interface speaks eDelivery
/// AS4 and this design exposes no inbound endpoint (<c>recommendations.md</c> §4.1). A Ground Officer
/// files in the EU Customs Trader Portal and the MRN is recorded, which is what these steps stand in
/// for (<c>docs/adr/0003</c>).
/// </para>
/// </remarks>
[Binding]
public sealed class EnsSteps(FreedomApiClient api, ScenarioState state)
{
    private const string ManifestKey = "manifest";

    private const string MrnKey = "ens-mrn";

    private string EnsPath(string suffix = "") =>
        $"/manifests/{state.Pinned(ManifestKey)}/ens{suffix}";

    /// <summary>
    /// A well-formed MRN, unique per scenario so parallel runs cannot read each other's.
    /// </summary>
    /// <remarks>
    /// Eighteen characters: two digits of year, the declaring country's ISO code, thirteen characters
    /// of reference and a check character, all upper case. Freedom checks that shape and not the check
    /// character — refusing an MRN ICS2 has already issued would strand a convoy over a bug of ours.
    /// </remarks>
    private static string NewMrn() =>
        "26FR" + Guid.NewGuid().ToString("N").ToUpperInvariant()[..14];

    /// <summary>
    /// The filing sheet the Ground Officer would take to the portal. Asserted here rather than in a
    /// component test as well, because this is the only place it is composed from a real database
    /// across three slices — manifest, convoy and boxes.
    /// </summary>
    [When("I GET the filing sheet for the remembered manifest")]
    public Task WhenIGetTheFilingSheet() =>
        api.SendAsync(HttpMethod.Get, EnsPath("/filing-sheet"), state.CurrentToken, null);

    /// <summary>
    /// The redaction, on the wire. An ENS needs the consignee's address; the sheet withholds it and
    /// says where to get it, because the filer is a Ground Officer and holds it already. A sheet
    /// listing Ukrainian delivery addresses would be a targeting document.
    /// </summary>
    [Then("the filing sheet withholds the delivery address and says where to get it")]
    public void ThenTheFilingSheetWithholdsTheAddress()
    {
        var sheet = JsonDocument.Parse(api.LastBody).RootElement;

        sheet.GetProperty("consigneeAddressSource").GetString()
            .Should().Be("GET /receivers/{ref}/detail");

        foreach (var consignment in sheet.GetProperty("consignments").EnumerateArray())
        {
            consignment.GetProperty("consigneeAddressWithheld").GetBoolean().Should().BeTrue();
        }

        foreach (var forbidden in new[] { "street", "postcode", "addressLine", "contactPhone", "contactName" })
        {
            api.LastBody.Should().NotContainEquivalentOf(
                forbidden, "the filing sheet must have nowhere to put a delivery address");
        }
    }

    [Then("the filing sheet declares a mode of transport and a gross mass")]
    public void ThenTheFilingSheetDeclaresTheCrossing()
    {
        var sheet = JsonDocument.Parse(api.LastBody).RootElement;

        // 1 is maritime and 3 is road. 2 is rail, which the Brexit Smart Border does not accept, so
        // a shuttle crossing is declared as road even though the lorry travels on a train.
        sheet.GetProperty("modeOfTransportCode").GetInt32().Should().BeOneOf(1, 3);
        sheet.GetProperty("grossMassKg").GetInt32().Should()
            .BeGreaterThan(0, "the body was: {0}", api.LastBody);
        sheet.GetProperty("passiveMeansOfTransport").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [When("I record an ICS2 declaration for the remembered manifest")]
    public async Task WhenIRecordADeclaration()
    {
        var mrn = NewMrn();
        state.Pin(MrnKey, mrn);

        await api.SendAsync(
            HttpMethod.Put,
            EnsPath(),
            state.CurrentToken,
            $$"""
              { "mrn": "{{mrn}}", "acceptedAt": "2026-08-24T09:30:00+00:00",
                "filedBy": "groundofficer", "filingReference": "STP-BDD" }
              """);
    }

    [When("I record the same ICS2 declaration again")]
    public Task WhenIRecordTheSameDeclarationAgain() =>
        api.SendAsync(
            HttpMethod.Put,
            EnsPath(),
            state.CurrentToken,
            $$"""
              { "mrn": "{{state.Pinned(MrnKey)}}", "acceptedAt": "2026-08-24T09:30:00+00:00",
                "filedBy": "groundofficer", "filingReference": "STP-BDD" }
              """);

    [When("I record a malformed ICS2 declaration for the remembered manifest")]
    public Task WhenIRecordAMalformedDeclaration() =>
        api.SendAsync(
            HttpMethod.Put,
            EnsPath(),
            state.CurrentToken,
            """
            { "mrn": "not-an-mrn", "acceptedAt": "2026-08-24T09:30:00+00:00",
              "filedBy": "groundofficer" }
            """);

    [When("I withdraw the ICS2 declaration for the remembered manifest")]
    public Task WhenIWithdrawTheDeclaration() =>
        api.SendAsync(HttpMethod.Delete, EnsPath(), state.CurrentToken, null);

    [When("I GET the ICS2 declaration for the remembered manifest")]
    public Task WhenIGetTheDeclaration() =>
        api.SendAsync(HttpMethod.Get, EnsPath(), state.CurrentToken, null);

    [Then("the recorded declaration is the one I filed")]
    public void ThenTheRecordedDeclarationIsMine()
    {
        var declaration = JsonDocument.Parse(api.LastBody).RootElement;

        declaration.GetProperty("mrn").GetString().Should().Be(state.Pinned(MrnKey));
        declaration.GetProperty("filedBy").GetString().Should().Be("groundofficer");
    }

    /// <summary>
    /// The whole point of the integration: the envelope names the declaration the crossing was
    /// actually accepted under, not the placeholder identifier real French customs refuses with
    /// FONC-ERR-004.
    /// </summary>
    [Then("the envelope names the declaration I recorded")]
    public async Task ThenTheEnvelopeNamesMyDeclaration()
    {
        var response = await api.SendAsync(
            HttpMethod.Get, $"/manifests/{state.Pinned(ManifestKey)}/elo", state.CurrentToken, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the body was: {0}", api.LastBody);

        // The envelope read model carries a count rather than the identifiers themselves — an ELO is
        // an index, and the API does not echo other systems' references back. So this asserts the one
        // thing the read model can prove: exactly one formality, which under TIR/ATA is the ENS.
        JsonDocument.Parse(api.LastBody).RootElement
            .GetProperty("declarationCount").GetInt32().Should().Be(1);
    }
}

using System.Net;
using AwesomeAssertions;
using Reqnroll;
using UA.Action.Freedom.Tests.BDD.Support;

namespace UA.Action.Freedom.Tests.BDD.Steps;

/// <summary>
/// Steps particular to <c>/donors</c> and <c>/donations</c>, and the fixtures the scenarios build on: a donor, and a
/// donation of theirs. The generic HTTP and authentication steps live in <see cref="ApiSteps"/>.
/// </summary>
/// <remarks>
/// Fixtures are made through the API as the operator, who holds the roles that enter donations, and are registered
/// for cleanup the way any created resource is. <c>{donor}</c> and <c>{donation}</c> in a path or body expand to the
/// remembered identifiers (see <see cref="CategoriesSteps.Expand"/>).
/// </remarks>
[Binding]
public sealed class DonationsSteps(FreedomApiClient api, ScenarioState state)
{
    internal const string DonorKey = "donor";

    internal const string DonationKey = "donation";

    [Given("I remember the donor")]
    public void GivenIRememberTheDonor() => state.Remember(DonorKey);

    [Given("I remember the donation")]
    public void GivenIRememberTheDonation() => state.Remember(DonationKey);

    [Given("a donor exists")]
    public async Task GivenADonorExists()
    {
        var operatorToken = await api.TokenForAsync("operator");
        var response = await api.SendAsync(
            HttpMethod.Post, "/donors", operatorToken,
            """{ "name": "BDD Donor", "email": "bdd.donor@example.org", "phone": "+447700900456" }""");

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);
        Track(response, "donors", DonorKey);
    }

    [Given("a donation exists")]
    public async Task GivenADonationExists()
    {
        var operatorToken = await api.TokenForAsync("operator");
        var response = await api.SendAsync(
            HttpMethod.Post, "/donations", operatorToken,
            $$"""{ "donorId": "{{state.Pinned(DonorKey)}}", "receivedOn": "2026-09-20", "notes": "BDD donation" }""");

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);
        Track(response, "donations", DonationKey);
    }

    private void Track(HttpResponseMessage response, string resource, string name)
    {
        var location = response.Headers.Location!;
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.ToString();
        var id = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];

        state.CreatedResources.Add((resource, id));
        state.Pin(name, id);
    }
}

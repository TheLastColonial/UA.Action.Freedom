using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EDI.ELO.Tests.Unit;

/// <summary>
/// What the three operations make of the bodies French customs actually sends.
/// </summary>
/// <remarks>
/// The barcode document is the whole point of an ELO — it is what a driver presents at the Smart
/// Border — and the committed spec models it as <c>pdf: { type: string, enum: [formatbytebase64] }</c>,
/// a single-value enum labelling an encoding rather than carrying content. The spec's own worked
/// examples send real base64 in that field, and they disagree with the schema about whether it sits
/// inside <c>enveloppe</c> or beside it, so both placements are pinned here.
/// <para>
/// Bodies are written out as literals rather than produced by serialising a DTO. Round-tripping
/// would only prove this code agrees with itself; the literal pins what the French customs ESB puts
/// on the wire.
/// </para>
/// </remarks>
public class EloEnvelopeResponseTests
{
    private const string TestBaseUrl = "https://elo.example.test/";
    private const string Jeton = "EI202512091201178668Z";
    private const string NumeroDossier = "B2025120912003386654";

    private static readonly string PdfBase64 =
        Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4 barcode for one lorry"));

    [Fact]
    public async Task Reads_the_barcode_document_the_envelope_carries()
    {
        var client = ClientReturning(HttpStatusCode.OK, $$"""
            {
              "enveloppe": {
                "jeton": "{{Jeton}}",
                "numeroDossier": "{{NumeroDossier}}",
                "statut": "FERMEE",
                "modeCreation": "EDI",
                "nombreDeclaration": 1,
                "procedureSecoursIcs2": false,
                "pdf": "{{PdfBase64}}"
              }
            }
            """);

        var created = await Create(client);

        created.Enveloppe!.Jeton.Should().Be(Jeton);
        created.Enveloppe.NumeroDossier.Should().Be(NumeroDossier);
        created.Enveloppe.Statut.Should().Be(StatutEnveloppe.FERMEE);
        created.Enveloppe.Pdf.Should().Be(PdfBase64);
    }

    /// <summary>
    /// The spec's example puts <c>pdf</c> beside <c>enveloppe</c> rather than inside it, so the
    /// response type binds it in both places and a caller reads whichever arrived.
    /// </summary>
    [Fact]
    public async Task Reads_a_barcode_document_sent_beside_the_envelope_rather_than_inside_it()
    {
        var client = ClientReturning(HttpStatusCode.OK, $$"""
            {
              "enveloppe": {
                "jeton": "{{Jeton}}",
                "numeroDossier": "{{NumeroDossier}}",
                "statut": "FERMEE"
              },
              "pdf": "{{PdfBase64}}"
            }
            """);

        var created = await Create(client);

        created.Pdf.Should().Be(PdfBase64);
        created.Enveloppe!.Pdf.Should().BeNull();
    }

    [Fact]
    public async Task Reads_a_retrieved_envelope_that_has_been_paired_and_embarked()
    {
        var client = ClientReturning(HttpStatusCode.OK, $$"""
            {
              "enveloppe": {
                "jeton": "{{Jeton}}",
                "numeroDossier": "{{NumeroDossier}}",
                "statut": "EMBARQUEE",
                "modeCreation": "EDI",
                "declarations": [
                  {
                    "identifiant": "25FR17551780961AT5",
                    "typeDeclaration": "ENS",
                    "informationsValidation": { "etat": "CONFORME", "code": "RETOUR-OK" }
                  }
                ],
                "nombreDeclaration": 1,
                "aUneELOValide": true,
                "pdf": "{{PdfBase64}}"
              }
            }
            """);

        var retrieved = await client.RecupererENVAsync(
            authorization: "Bearer token",
            messageCode: "ENV_REC01",
            functionalId: NumeroDossier,
            messageId: Guid.NewGuid().ToString(),
            correlationId: Guid.NewGuid().ToString(),
            body: new ENV_REC01 { NumeroDossier = NumeroDossier },
            cancellationToken: TestContext.Current.CancellationToken);

        retrieved.Enveloppe!.Statut.Should().Be(StatutEnveloppe.EMBARQUEE);
        retrieved.Enveloppe.AUneELOValide.Should().BeTrue();
        retrieved.Enveloppe.Pdf.Should().Be(PdfBase64);
        retrieved.Enveloppe.Declarations.Should().ContainSingle()
            .Which.TypeDeclaration.Should().Be(TypeDeclaration.ENS);
    }

    [Fact]
    public async Task Reads_a_modified_envelope()
    {
        var handler = new StubHandler(HttpStatusCode.OK, $$"""
            { "enveloppe": { "jeton": "{{Jeton}}", "numeroDossier": "{{NumeroDossier}}", "statut": "FERMEE" } }
            """);

        var modified = await ClientFor(handler).ModifierENVAsync(
            authorization: "Bearer token",
            messageCode: "ENV_MOD01",
            functionalId: NumeroDossier,
            messageId: Guid.NewGuid().ToString(),
            correlationId: Guid.NewGuid().ToString(),
            body: new ENV_MOD01
            {
                NumeroDossier = NumeroDossier,
                IdentifiantsDeclarationAAjouter = ["25TR341200096251M7"],
            },
            cancellationToken: TestContext.Current.CancellationToken);

        handler.LastRequest!.RequestUri.Should().Be(TestBaseUrl + "enveloppe/modifier");
        modified.Enveloppe!.NumeroDossier.Should().Be(NumeroDossier);
    }

    /// <summary>
    /// A rejection is a modelled response, not a transport failure: the error code and label are
    /// what tell an operator whether to fix the declaration or retry later.
    /// </summary>
    [Fact]
    public async Task Surfaces_a_refusal_with_the_code_and_label_french_customs_gave()
    {
        var client = ClientReturning(HttpStatusCode.BadRequest, """
            {
              "informationsErreur": {
                "statut": "FONC-ERR-004",
                "libelleErreur": "Impossible de fermer l'enveloppe. Code erreur UCENV_CTR_RG01."
              }
            }
            """);

        var act = () => Create(client);

        var refusal = await act.Should().ThrowAsync<EloApiException<ENV_CRE03>>();
        refusal.Which.StatusCode.Should().Be(400);
        refusal.Which.Result.InformationsErreur!.Statut.Should().Be("FONC-ERR-004");
        refusal.Which.Result.InformationsErreur.LibelleErreur.Should().Contain("UCENV_CTR_RG01");
    }

    private static Task<ENV_CRE02> Create(IEloClient client) => client.CreerENVAsync(
        authorization: "Bearer token",
        messageCode: "ENV_CRE01",
        functionalId: "corr-1",
        messageId: Guid.NewGuid().ToString(),
        correlationId: "corr-1",
        body: new ENV_CRE01 { IdentifiantsDeclaration = ["25FR17551780961AT5"] },
        cancellationToken: TestContext.Current.CancellationToken);

    private static IEloClient ClientReturning(HttpStatusCode status, string body) =>
        ClientFor(new StubHandler(status, body));

    private static IEloClient ClientFor(StubHandler handler) =>
        new ServiceCollection()
            .AddEloClient(o => o.BaseUrl = new Uri(TestBaseUrl))
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .Services
            .BuildServiceProvider()
            .GetRequiredService<IEloClient>();

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}

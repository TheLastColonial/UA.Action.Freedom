using System.Text;
using AwesomeAssertions;
using EDI.ELO;
using MELT;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UA.Action.Freedom.CustomsWorker.Elo;
using UA.Action.Freedom.CustomsWorker.Queueing;

namespace UA.Action.Freedom.Tests.Unit.CustomsWorker;

/// <summary>
/// Creating a French customs logistics envelope for one vehicle, and deciding what becomes of the
/// queue message afterwards.
/// </summary>
/// <remarks>
/// The interesting decision is never "how do I call the ELO API" — the SDK does that — but when the
/// message may be deleted. Three dispositions, and each wrong choice costs something different:
/// completing a message French customs never accepted loses a vehicle's paperwork silently;
/// poisoning one that failed on a timeout throws away a request nobody recorded elsewhere; leaving
/// one that will never succeed keeps a worker busy for ever.
///
/// <para>
/// There is a fourth rule peculiar to this integration: <strong>nothing is dead-lettered after
/// French customs has accepted the envelope.</strong> Creation is not idempotent and the envelope
/// exists at customs from that moment; discarding the message would leave an orphan nobody in
/// Freedom can name, modify or present. So a bad barcode is stored-as-best-we-can and completed,
/// never poisoned.
/// </para>
/// </remarks>
public class EloEnvelopeProcessorTests
{
    private const string ManifestId = "MAN-0001";
    private const string Jeton = "EI202512091201178668Z";
    private const string NumeroDossier = "B2025120912003386654";

    private static readonly byte[] Barcode = Encoding.ASCII.GetBytes("%PDF-1.4 barcode");

    /// <summary>
    /// Written as a literal rather than produced by a serialiser, deliberately. Round-tripping
    /// proves only that this code agrees with itself: it would keep passing while the producer wrote
    /// camelCase and the consumer expected PascalCase, which is exactly the mismatch that reaches
    /// the queue and silently poisons every message. The literal pins the wire contract instead.
    /// </summary>
    private const string QueuedEnvelopeJson =
        """
        {
          "manifestId": "MAN-0001",
          "crossingDirection": "Import",
          "lorryType": "Loaded",
          "tirAta": true,
          "hasTransportContract": false,
          "postal": false,
          "emptyPackaging": false,
          "sanitaryOrPhytosanitary": false,
          "fisheryProducts": false,
          "declarationIdentifiers": [ "25FR17551780961AT5" ]
        }
        """;

    private static EloWorkItem AQueuedEnvelope(string body = QueuedEnvelopeJson) =>
        new("1", "receipt", body);

    [Fact]
    public async Task Creates_an_envelope_for_a_queued_manifest()
    {
        var elo = AnEloReturning(AnEnvelope());
        var documents = Substitute.For<IEloDocumentStore>();

        var processed = await ProcessorFor(elo, documents, AQueuedEnvelope())
            .ProcessNextAsync(TestContext.Current.CancellationToken);

        processed.Should().BeTrue();
        await elo.Received(1).CreerENVAsync(
            Arg.Any<string>(),
            "ENV_CRE01",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<ENV_CRE01>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The pairing information is what French customs uses to decide which formalities the envelope
    /// must contain, so every flag has to survive the trip from the approval that queued it.
    /// </summary>
    [Fact]
    public async Task Declares_the_crossing_exactly_as_the_approval_described_it()
    {
        var elo = AnEloReturning(AnEnvelope());
        ENV_CRE01? sent = null;
        await elo.CreerENVAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Do<ENV_CRE01>(body => sent = body), Arg.Any<CancellationToken>());

        await ProcessorFor(elo, Substitute.For<IEloDocumentStore>(), AQueuedEnvelope())
            .ProcessNextAsync(TestContext.Current.CancellationToken);

        sent!.InformationsAppairage!.SensTraversee.Should().Be(SensTraversee.IMPORT);
        sent.InformationsAppairage.TypeCamion.Should().Be(TypeCamion.PLEIN);
        sent.InformationsAppairage.EstTIRATA.Should().BeTrue();
        sent.InformationsAppairage.PossedeContratTransport.Should().BeFalse();
        sent.InformationsAppairage.EstSPS.Should().BeFalse();
        sent.InformationsAppairage.EstProduitPeche.Should().BeFalse();
        sent.IdentifiantsDeclaration.Should().Equal("25FR17551780961AT5");
    }

    [Fact]
    public async Task Stores_the_envelope_and_the_barcode_the_driver_presents()
    {
        var documents = Substitute.For<IEloDocumentStore>();
        EloEnvelopeDocument? stored = null;
        byte[]? pdf = null;
        await documents.SaveAsync(
            Arg.Do<EloEnvelopeDocument>(document => stored = document),
            Arg.Do<byte[]?>(bytes => pdf = bytes),
            Arg.Any<CancellationToken>());

        await ProcessorFor(AnEloReturning(AnEnvelope()), documents, AQueuedEnvelope())
            .ProcessNextAsync(TestContext.Current.CancellationToken);

        stored!.ManifestId.Should().Be(ManifestId);
        stored.Jeton.Should().Be(Jeton);
        stored.NumeroDossier.Should().Be(NumeroDossier);
        stored.Statut.Should().Be(nameof(StatutEnveloppe.FERMEE));
        stored.DeclarationCount.Should().Be(1);
        stored.HasBarcodeDocument.Should().BeTrue();
        pdf.Should().Equal(Barcode);
    }

    [Fact]
    public async Task Removes_the_work_item_from_the_queue_once_the_envelope_is_stored()
    {
        var queue = QueueReturning(AQueuedEnvelope());

        await ProcessorFor(AnEloReturning(AnEnvelope()), Substitute.For<IEloDocumentStore>(), queue: queue)
            .ProcessNextAsync(TestContext.Current.CancellationToken);

        await queue.Received(1).CompleteAsync(Arg.Any<EloWorkItem>(), Arg.Any<CancellationToken>());
        await queue.DidNotReceive().DeadLetterAsync(
            Arg.Any<EloWorkItem>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Leaves_the_work_item_on_the_queue_when_french_customs_cannot_be_reached()
    {
        var elo = Substitute.For<IEloClient>();
        elo.CreerENVAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ENV_CRE01>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));
        var queue = QueueReturning(AQueuedEnvelope());

        await ProcessorFor(elo, Substitute.For<IEloDocumentStore>(), queue: queue)
            .ProcessNextAsync(TestContext.Current.CancellationToken);

        await queue.DidNotReceive().CompleteAsync(Arg.Any<EloWorkItem>(), Arg.Any<CancellationToken>());
        await queue.DidNotReceive().DeadLetterAsync(
            Arg.Any<EloWorkItem>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A 400 from French customs is a judgement on the envelope, not a transport failure — a
    /// declaration in the wrong format, one already paired to another lorry, or a crossing direction
    /// the formality does not match. Retrying produces the same answer, so it needs a person.
    /// </summary>
    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(422)]
    public async Task Moves_the_work_item_to_the_poison_queue_when_french_customs_refuses_the_envelope(int status)
    {
        var elo = AnEloRefusing(status);
        var queue = QueueReturning(AQueuedEnvelope());

        await ProcessorFor(elo, Substitute.For<IEloDocumentStore>(), queue: queue)
            .ProcessNextAsync(TestContext.Current.CancellationToken);

        await queue.Received(1).DeadLetterAsync(
            Arg.Any<EloWorkItem>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await queue.DidNotReceive().CompleteAsync(Arg.Any<EloWorkItem>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A 5xx is French customs being unwell, not an answer about the envelope.
    /// </summary>
    [Fact]
    public async Task Retries_a_server_error_rather_than_poisoning_the_request()
    {
        var elo = AnEloRefusing(503);
        var queue = QueueReturning(AQueuedEnvelope());

        await ProcessorFor(elo, Substitute.For<IEloDocumentStore>(), queue: queue)
            .ProcessNextAsync(TestContext.Current.CancellationToken);

        await queue.DidNotReceive().DeadLetterAsync(
            Arg.Any<EloWorkItem>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await queue.DidNotReceive().CompleteAsync(Arg.Any<EloWorkItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Moves_an_unreadable_work_item_to_the_poison_queue_without_calling_french_customs()
    {
        var elo = Substitute.For<IEloClient>();
        var queue = QueueReturning(AQueuedEnvelope("{ not json"));

        await ProcessorFor(elo, Substitute.For<IEloDocumentStore>(), queue: queue)
            .ProcessNextAsync(TestContext.Current.CancellationToken);

        await queue.Received(1).DeadLetterAsync(
            Arg.Any<EloWorkItem>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await elo.DidNotReceive().CreerENVAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<ENV_CRE01>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Moves_a_work_item_with_no_manifest_reference_to_the_poison_queue()
    {
        var elo = Substitute.For<IEloClient>();
        var queue = QueueReturning(AQueuedEnvelope("""{ "crossingDirection": "Import" }"""));

        await ProcessorFor(elo, Substitute.For<IEloDocumentStore>(), queue: queue)
            .ProcessNextAsync(TestContext.Current.CancellationToken);

        await queue.Received(1).DeadLetterAsync(
            Arg.Any<EloWorkItem>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await elo.DidNotReceive().CreerENVAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<ENV_CRE01>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// ENV_CTR_RG08: a loaded transport unit must name at least one formality, and French customs
    /// answers an envelope that names none with FONC-ERR-004. Caught before the call, because the
    /// answer is already known and a refused create still consumes a request.
    /// </summary>
    [Fact]
    public async Task Refuses_a_loaded_lorry_that_names_no_declaration_before_calling_french_customs()
    {
        var elo = Substitute.For<IEloClient>();
        var queue = QueueReturning(AQueuedEnvelope(
            """
            { "manifestId": "MAN-0001", "crossingDirection": "Import", "lorryType": "Loaded",
              "tirAta": true, "declarationIdentifiers": [] }
            """));

        await ProcessorFor(elo, Substitute.For<IEloDocumentStore>(), queue: queue)
            .ProcessNextAsync(TestContext.Current.CancellationToken);

        await queue.Received(1).DeadLetterAsync(
            Arg.Any<EloWorkItem>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await elo.DidNotReceive().CreerENVAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<ENV_CRE01>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The envelope exists at French customs the moment the create succeeds, and creating one is not
    /// idempotent. Poisoning the message here would strand an envelope nobody in Freedom can name,
    /// so the reference is stored without its barcode and the message is completed. The barcode can
    /// be re-fetched; a forgotten <c>numeroDossier</c> cannot.
    /// </summary>
    [Fact]
    public async Task Keeps_an_envelope_whose_barcode_could_not_be_decoded()
    {
        var elo = AnEloReturning(AnEnvelope(pdf: "this is not base64 !!"));
        var documents = Substitute.For<IEloDocumentStore>();
        EloEnvelopeDocument? stored = null;
        byte[]? pdf = null;
        await documents.SaveAsync(
            Arg.Do<EloEnvelopeDocument>(document => stored = document),
            Arg.Do<byte[]?>(bytes => pdf = bytes),
            Arg.Any<CancellationToken>());
        var queue = QueueReturning(AQueuedEnvelope());

        await ProcessorFor(elo, documents, queue: queue)
            .ProcessNextAsync(TestContext.Current.CancellationToken);

        stored!.NumeroDossier.Should().Be(NumeroDossier);
        stored.HasBarcodeDocument.Should().BeFalse();
        pdf.Should().BeNull();
        await queue.Received(1).CompleteAsync(Arg.Any<EloWorkItem>(), Arg.Any<CancellationToken>());
        await queue.DidNotReceive().DeadLetterAsync(
            Arg.Any<EloWorkItem>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The spec's examples put <c>pdf</c> beside <c>enveloppe</c> while its schema puts it inside, so
    /// the processor reads whichever arrived rather than picking a side.
    /// </summary>
    [Fact]
    public async Task Reads_a_barcode_sent_beside_the_envelope_rather_than_inside_it()
    {
        var response = AnEnvelope(pdf: null);
        response.Pdf = Convert.ToBase64String(Barcode);
        var documents = Substitute.For<IEloDocumentStore>();
        byte[]? pdf = null;
        await documents.SaveAsync(
            Arg.Any<EloEnvelopeDocument>(), Arg.Do<byte[]?>(bytes => pdf = bytes), Arg.Any<CancellationToken>());

        await ProcessorFor(AnEloReturning(response), documents, AQueuedEnvelope())
            .ProcessNextAsync(TestContext.Current.CancellationToken);

        pdf.Should().Equal(Barcode);
    }

    /// <summary>
    /// Storing failed, so nothing in Freedom records the envelope yet. Leaving the message means the
    /// next attempt creates a second envelope at customs, which is unwanted but recoverable;
    /// completing it would lose the first one for good.
    /// </summary>
    [Fact]
    public async Task Leaves_the_work_item_when_the_envelope_could_not_be_stored()
    {
        var documents = Substitute.For<IEloDocumentStore>();
        documents.SaveAsync(Arg.Any<EloEnvelopeDocument>(), Arg.Any<byte[]?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("storage unavailable"));
        var queue = QueueReturning(AQueuedEnvelope());

        await ProcessorFor(AnEloReturning(AnEnvelope()), documents, queue: queue)
            .ProcessNextAsync(TestContext.Current.CancellationToken);

        await queue.DidNotReceive().CompleteAsync(Arg.Any<EloWorkItem>(), Arg.Any<CancellationToken>());
        await queue.DidNotReceive().DeadLetterAsync(
            Arg.Any<EloWorkItem>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_nothing_at_all_when_the_queue_is_empty()
    {
        var queue = Substitute.For<IEloWorkQueue>();
        queue.ReceiveAsync(Arg.Any<CancellationToken>()).Returns((EloWorkItem?)null);
        var elo = Substitute.For<IEloClient>();

        var processed = await ProcessorFor(elo, Substitute.For<IEloDocumentStore>(), queue: queue)
            .ProcessNextAsync(TestContext.Current.CancellationToken);

        processed.Should().BeFalse();
        await elo.DidNotReceive().CreerENVAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<ENV_CRE01>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// <see cref="EloApiException"/> carries up to the whole response body in its message, and a
    /// French customs error label quotes the declaration it objected to. The status and error code
    /// are what an operator needs; the body is not worth retaining in a log that is.
    /// </summary>
    [Fact]
    public async Task Logs_the_refusal_code_but_never_what_french_customs_sent_back()
    {
        var logs = TestLoggerFactory.Create();
        var elo = AnEloRefusing(400, libelleErreur: "Impossible de fermer l'enveloppe pour Olena Kovalenko, Kharkiv.");

        await ProcessorFor(elo, Substitute.For<IEloDocumentStore>(), AQueuedEnvelope(), loggerFactory: logs)
            .ProcessNextAsync(TestContext.Current.CancellationToken);

        var written = string.Join(" ", logs.Sink.LogEntries.Select(entry => $"{entry.Message} {entry.Exception}"));
        written.Should().NotContain("Kovalenko").And.NotContain("Kharkiv");
        written.Should().Contain("400").And.Contain("FONC-ERR-004");
    }

    private static EloEnvelopeProcessor ProcessorFor(
        IEloClient elo,
        IEloDocumentStore documents,
        EloWorkItem? item = null,
        IEloWorkQueue? queue = null,
        ITestLoggerFactory? loggerFactory = null)
    {
        queue ??= QueueReturning(item);

        var tokens = Substitute.For<IEloTokenProvider>();
        tokens.GetAsync(Arg.Any<CancellationToken>()).Returns("a-token");

        return new EloEnvelopeProcessor(
            queue,
            elo,
            tokens,
            documents,
            (loggerFactory ?? TestLoggerFactory.Create()).CreateLogger<EloEnvelopeProcessor>());
    }

    private static IEloWorkQueue QueueReturning(EloWorkItem? item)
    {
        var queue = Substitute.For<IEloWorkQueue>();
        queue.ReceiveAsync(Arg.Any<CancellationToken>()).Returns(item);
        return queue;
    }

    private static ENV_CRE02 AnEnvelope(string? pdf = null) => new()
    {
        Enveloppe = new Enveloppe
        {
            Jeton = Jeton,
            NumeroDossier = NumeroDossier,
            Statut = StatutEnveloppe.FERMEE,
            ModeCreation = ModeCreation.EDI,
            NombreDeclaration = 1,
            Pdf = pdf ?? Convert.ToBase64String(Barcode),
        },
    };

    private static IEloClient AnEloReturning(ENV_CRE02 response)
    {
        var elo = Substitute.For<IEloClient>();
        elo.CreerENVAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ENV_CRE01>(), Arg.Any<CancellationToken>())
            .Returns(response);
        return elo;
    }

    private static IEloClient AnEloRefusing(int status, string libelleErreur = "Déclaration refusée.")
    {
        var elo = Substitute.For<IEloClient>();
        elo.CreerENVAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ENV_CRE01>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new EloApiException<ENV_CRE03>(
                "Réponse KO",
                status,
                libelleErreur,
                new Dictionary<string, IEnumerable<string>>(),
                new ENV_CRE03
                {
                    InformationsErreur = new ReponseMessageEnveloppeErreur
                    {
                        Statut = "FONC-ERR-004",
                        LibelleErreur = libelleErreur,
                    },
                },
                null));
        return elo;
    }
}

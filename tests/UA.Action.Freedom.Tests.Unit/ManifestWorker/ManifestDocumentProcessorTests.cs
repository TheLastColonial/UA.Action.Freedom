using AwesomeAssertions;
using MELT;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UA.Action.Freedom.ManifestWorker.Documents;
using UA.Action.Freedom.ManifestWorker.Queueing;

namespace UA.Action.Freedom.Tests.Unit.ManifestWorker;

/// <summary>
/// Rendering the document that travels with a vehicle, and the queue rules around it.
/// </summary>
/// <remarks>
/// Two things matter here. The first is what the document says: it crosses several borders where
/// it may be inspected, photographed or seized, and one listing precise Ukrainian delivery
/// addresses is a targeting document (docs/domain/key-concepts.md § Data Sensitivity). The
/// second is when a message leaves the queue, because a manifest that is never rendered is a
/// vehicle at a border with no paperwork.
/// </remarks>
public class ManifestDocumentProcessorTests
{
    /// <summary>
    /// Written out as a literal rather than produced by a serialiser, deliberately.
    /// Round-tripping through the serialiser proves only that this code agrees with itself: it
    /// would keep passing while the Freedom Application wrote one shape and this worker expected
    /// another. The literal pins the wire contract instead.
    /// </summary>
    private const string QueuedRequestJson =
        """
        {
          "manifestId": "MAN-0001",
          "vehicleRegistration": "AB12CDE",
          "vehicleWeightKg": 1400,
          "cargoKg": 42,
          "crewAndBagsKg": 200,
          "fuelKg": 45,
          "totalKg": 1687,
          "lines": [
            {
              "boxId": 1,
              "weightKg": 30,
              "itemCount": 4,
              "receiverOrganisation": "Kharkiv Regional Hospital",
              "receiverRegion": "Kharkiv oblast"
            },
            {
              "boxId": 2,
              "weightKg": 12,
              "itemCount": 2,
              "receiverOrganisation": "Poltava Childrens Home",
              "receiverRegion": "Poltava oblast"
            }
          ]
        }
        """;

    private static ManifestDocumentWorkItem AQueuedRequest(string? body = null) =>
        new("1", "receipt", body ?? QueuedRequestJson);

    private static ManifestDocumentProcessor ProcessorFor(
        IManifestDocumentStore documents,
        ManifestDocumentWorkItem? item,
        IManifestDocumentQueue? queue = null,
        ITestLoggerFactory? loggerFactory = null)
    {
        queue ??= Substitute.For<IManifestDocumentQueue>();
        queue.ReceiveAsync(Arg.Any<CancellationToken>()).Returns(item);

        return new ManifestDocumentProcessor(
            queue,
            documents,
            (loggerFactory ?? TestLoggerFactory.Create()).CreateLogger<ManifestDocumentProcessor>());
    }

    private static async Task<string> RenderedDocumentAsync()
    {
        var documents = Substitute.For<IManifestDocumentStore>();
        string? stored = null;
        await documents.SaveAsync(
            Arg.Any<string>(),
            Arg.Do<string>(content => stored = content),
            Arg.Any<CancellationToken>());

        var processor = ProcessorFor(documents, AQueuedRequest());
        await processor.ProcessNextAsync(CancellationToken.None);

        return stored ?? string.Empty;
    }

    [Fact]
    public async Task Renders_and_stores_the_document_for_a_queued_manifest()
    {
        var documents = Substitute.For<IManifestDocumentStore>();
        var processor = ProcessorFor(documents, AQueuedRequest());

        var processed = await processor.ProcessNextAsync(CancellationToken.None);

        processed.Should().BeTrue();
        await documents.Received(1).SaveAsync("MAN-0001", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_document_shows_the_cargo_the_weights_and_a_region()
    {
        var document = await RenderedDocumentAsync();

        document.Should().Contain("MAN-0001").And.Contain("AB12CDE");
        document.Should().Contain("Kharkiv Regional Hospital").And.Contain("Kharkiv oblast");
        document.Should().Contain("1687 kg");
        document.Should().Contain("30 kg").And.Contain("12 kg");
    }

    [Fact]
    public async Task The_document_never_carries_a_delivery_address_or_a_contact()
    {
        // The rule this whole worker exists to keep. Asserted on the rendered output rather than
        // on the mapping, because it is the output that crosses the border.
        var document = await RenderedDocumentAsync();

        document.Should().NotContain("Vulytsia").And.NotContain("Olena Kovalenko").And.NotContain("+380");
    }

    [Fact]
    public void The_document_request_has_nowhere_to_put_an_address()
    {
        // Structural, and the reason the test above can stay true. A later change cannot leak an
        // address through this worker without first adding a field to carry one — which is a
        // change a reviewer sees.
        var lineFields = typeof(ManifestDocumentLine).GetProperties().Select(property => property.Name);

        lineFields.Should().BeEquivalentTo(
            "BoxId", "WeightKg", "ItemCount", "ReceiverOrganisation", "ReceiverRegion");
    }

    [Fact]
    public async Task The_document_says_out_loud_what_it_does_not_contain()
    {
        // So nobody carrying it thinks it is incomplete and writes the address on by hand.
        var document = await RenderedDocumentAsync();

        document.Should().Contain("deliberately not shown");
        document.Should().Contain("released to the driver at the point of delivery");
    }

    [Fact]
    public async Task Removes_the_work_item_once_the_document_is_stored()
    {
        var queue = Substitute.For<IManifestDocumentQueue>();
        var item = AQueuedRequest();
        var processor = ProcessorFor(Substitute.For<IManifestDocumentStore>(), item, queue);

        await processor.ProcessNextAsync(CancellationToken.None);

        await queue.Received(1).CompleteAsync(item, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Leaves_the_work_item_on_the_queue_when_storage_cannot_be_reached()
    {
        // A manifest that is never rendered is a vehicle at a border with no paperwork, so the
        // message stays put and the visibility timeout brings it back.
        var documents = Substitute.For<IManifestDocumentStore>();
        documents.SaveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("storage unavailable"));
        var queue = Substitute.For<IManifestDocumentQueue>();
        var item = AQueuedRequest();
        var processor = ProcessorFor(documents, item, queue);

        var processed = await processor.ProcessNextAsync(CancellationToken.None);

        processed.Should().BeTrue();
        await queue.DidNotReceive().CompleteAsync(Arg.Any<ManifestDocumentWorkItem>(), Arg.Any<CancellationToken>());
        await queue.DidNotReceive().DeadLetterAsync(
            Arg.Any<ManifestDocumentWorkItem>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Moves_an_unreadable_work_item_to_the_poison_queue_without_writing_anything()
    {
        var documents = Substitute.For<IManifestDocumentStore>();
        var queue = Substitute.For<IManifestDocumentQueue>();
        var item = AQueuedRequest("this is not json");
        var processor = ProcessorFor(documents, item, queue);

        var processed = await processor.ProcessNextAsync(CancellationToken.None);

        processed.Should().BeTrue();
        await queue.Received(1).DeadLetterAsync(item, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await documents.DidNotReceive().SaveAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Moves_a_request_with_no_manifest_reference_to_the_poison_queue()
    {
        var queue = Substitute.For<IManifestDocumentQueue>();
        var item = AQueuedRequest("""{ "manifestId": "", "lines": [] }""");
        var processor = ProcessorFor(Substitute.For<IManifestDocumentStore>(), item, queue);

        await processor.ProcessNextAsync(CancellationToken.None);

        await queue.Received(1).DeadLetterAsync(item, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_nothing_at_all_when_the_queue_is_empty()
    {
        var processor = ProcessorFor(Substitute.For<IManifestDocumentStore>(), item: null);

        var processed = await processor.ProcessNextAsync(CancellationToken.None);

        processed.Should().BeFalse();
    }

    [Fact]
    public async Task Never_logs_the_consignee_a_manifest_carries()
    {
        // Logs are retained and reach the telemetry container (recommendations §4.8). The
        // message identifier is safe to log; the body is not.
        var logger = TestLoggerFactory.Create();
        var documents = Substitute.For<IManifestDocumentStore>();
        documents.SaveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("storage unavailable"));
        var processor = ProcessorFor(documents, AQueuedRequest(), loggerFactory: logger);

        await processor.ProcessNextAsync(CancellationToken.None);

        var written = string.Join(" ", logger.Sink.LogEntries.Select(entry => entry.Message));
        written.Should().NotContain("Kharkiv Regional Hospital").And.NotContain("Kharkiv oblast");
        written.Should().Contain("MAN-0001");
    }

    [Fact]
    public void A_manifest_with_no_cargo_still_renders_a_document()
    {
        // A vehicle carrying nothing but itself still crosses a border and still needs paperwork.
        var document = ManifestDocumentRenderer.Render(
            new ManifestDocumentRequest("MAN-0002", "AB12CDE", 1_400, 0, 200, 45, 1_645, []));

        document.Should().Contain("No boxes are loaded against this manifest.");
        document.Should().Contain("1645 kg");
    }
}

using Azure.Storage.Blobs;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Api.Documents;
using UA.Action.Freedom.Application.Declarations;

namespace UA.Action.Freedom.Tests.Integration.Declarations;

/// <summary>
/// The recorded ENS declaration detail store, against a real storage account.
/// </summary>
/// <remarks>
/// The write-once rule cannot be proved anywhere else, because storage enforces it rather than this
/// code. The declaration row gets it from a conditional <c>UPDATE</c>; a blob has no <c>WHERE</c> clause,
/// so the equivalent is a conditional create — and a test that substituted the blob client would only
/// prove this code passes a condition, not that the condition is the one the service honours.
/// </remarks>
[Trait("Category", "Integration")]
public class EnsDeclarationStoreTests
{
    private const string DefaultConnectionString =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;"
        + "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;"
        + "BlobEndpoint=http://localhost:10000/devstoreaccount1;"
        + "QueueEndpoint=http://localhost:10001/devstoreaccount1;";

    private const string Mrn = "25FR17551780961AT5";

    private const string Container = "ens";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("Storage__ConnectionString") ?? DefaultConnectionString;

    /// <summary>
    /// Probes the container rather than the account, the way <c>ConnectOrSkipAsync</c> probes the
    /// table rather than the server: an account that is up but has never had <c>tofu apply</c> run
    /// against it would otherwise fail every test here for the wrong reason.
    /// </summary>
    private static async Task<BlobServiceClient> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        var blobs = new BlobServiceClient(ConnectionString);
        bool present;

        try
        {
            present = await blobs.GetBlobContainerClient(Container).ExistsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            Assert.Skip($"Storage account is not reachable: {exception.Message}");
            throw;
        }

        Assert.SkipUnless(present, $"Blob container '{Container}' does not exist. Has `tofu apply` run?");

        return blobs;
    }

    private static BlobEnsDeclarationStore StoreOn(BlobServiceClient blobs) =>
        new(blobs, Options.Create(new StorageOptions
        {
            ConnectionString = ConnectionString,
            EnsContainer = Container,
        }));

    /// <summary>A declaration id nothing else in the suite uses, so runs do not collide.</summary>
    private static int ADeclarationId() => Random.Shared.Next(1_000_000, int.MaxValue);

    private static EnsDeclarationReadModel ADeclaration(int declarationId, string mrn = Mrn) =>
        new(declarationId, mrn, new DateTimeOffset(2026, 8, 24, 9, 30, 0, TimeSpan.Zero), "groundofficer", "STP-1");

    [Fact]
    public async Task Records_a_declaration_and_reads_it_back()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = StoreOn(await ConnectOrSkipAsync(cancellationToken));
        var id = ADeclarationId();

        var saved = await store.SaveAsync(ADeclaration(id), cancellationToken);
        var read = await store.GetAsync(id, cancellationToken);

        saved.Should().BeTrue();
        read.Should().Be(ADeclaration(id));
    }

    [Fact]
    public async Task Reports_no_declaration_for_an_id_that_has_none()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = StoreOn(await ConnectOrSkipAsync(cancellationToken));

        var read = await store.GetAsync(ADeclarationId(), cancellationToken);

        read.Should().BeNull();
    }

    /// <summary>
    /// The conditional create, which is the whole point of this file. Storage refuses the second
    /// write, so two dispatchers recording different MRNs at once resolve to one declaration — and
    /// the one that was already there is the one that survives.
    /// </summary>
    [Fact]
    public async Task Refuses_a_second_declaration_and_leaves_the_first_untouched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = StoreOn(await ConnectOrSkipAsync(cancellationToken));
        var id = ADeclarationId();
        await store.SaveAsync(ADeclaration(id), cancellationToken);

        var second = await store.SaveAsync(ADeclaration(id, "26GB99999999999ZZ9"), cancellationToken);
        var read = await store.GetAsync(id, cancellationToken);

        second.Should().BeFalse();
        read!.Mrn.Should().Be(Mrn);
    }

    /// <summary>
    /// A withdrawn declaration keeps its blob under its own id, and its replacement is a new declaration
    /// with a new id, so the old MRN stays as history without being moved.
    /// </summary>
    [Fact]
    public async Task A_replacement_declaration_does_not_disturb_the_one_it_replaces()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = StoreOn(await ConnectOrSkipAsync(cancellationToken));
        var withdrawn = ADeclarationId();
        var replacement = withdrawn + 1;
        await store.SaveAsync(ADeclaration(withdrawn), cancellationToken);

        var refiled = await store.SaveAsync(ADeclaration(replacement, "26GB99999999999ZZ9"), cancellationToken);

        refiled.Should().BeTrue();
        (await store.GetAsync(withdrawn, cancellationToken))!.Mrn.Should().Be(Mrn);
        (await store.GetAsync(replacement, cancellationToken))!.Mrn.Should().Be("26GB99999999999ZZ9");
    }
}

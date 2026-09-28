using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Api.Documents;
using UA.Action.Freedom.Application.Manifests;

namespace UA.Action.Freedom.Tests.Integration.Manifests;

/// <summary>
/// The recorded ENS declaration store, against a real storage account.
/// </summary>
/// <remarks>
/// Two rules here cannot be proved anywhere else, because both are enforced by storage rather than by
/// this code.
///
/// <para>
/// The first is write-once. <c>Manifest.GmrSubmittedAt</c> gets that from a conditional
/// <c>UPDATE</c>, so the database settles a race between two dispatchers pressing the same button.
/// A blob has no <c>WHERE</c> clause, so the equivalent is a conditional create — and a test that
/// substituted the blob client would only prove this code passes a condition, not that the condition
/// is the one the service honours.
/// </para>
///
/// <para>
/// The second is that superseding copies before it deletes, the same ordering rule and the same
/// reason as <c>AzureEloWorkQueue.DeadLetterAsync</c>: if the process dies between the two the
/// record still exists. Several ENS fields are non-amendable, so invalidate-and-refile is the normal
/// correction path, and the superseded MRN is what a customs query months later will be about.
/// </para>
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

    /// <summary>A manifest reference nothing else in the suite uses, so runs do not collide.</summary>
    private static string AManifestId() => $"MAN-ENS-{Guid.NewGuid():N}"[..20];

    private static EnsDeclarationReadModel ADeclaration(string manifestId, string mrn = Mrn) =>
        new(manifestId, mrn, new DateTimeOffset(2026, 8, 24, 9, 30, 0, TimeSpan.Zero), "groundofficer", "STP-1");

    [Fact]
    public async Task Records_a_declaration_and_reads_it_back()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var blobs = await ConnectOrSkipAsync(cancellationToken);
        var store = StoreOn(blobs);
        var id = AManifestId();

        var saved = await store.SaveAsync(ADeclaration(id), cancellationToken);
        var read = await store.GetAsync(id, cancellationToken);

        saved.Should().BeTrue();
        read.Should().Be(ADeclaration(id));
    }

    [Fact]
    public async Task Reports_no_declaration_for_a_manifest_that_has_none()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = StoreOn(await ConnectOrSkipAsync(cancellationToken));

        var read = await store.GetAsync(AManifestId(), cancellationToken);

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
        var id = AManifestId();
        await store.SaveAsync(ADeclaration(id), cancellationToken);

        var second = await store.SaveAsync(ADeclaration(id, "26GB99999999999ZZ9"), cancellationToken);
        var read = await store.GetAsync(id, cancellationToken);

        second.Should().BeFalse();
        read!.Mrn.Should().Be(Mrn);
    }

    /// <summary>
    /// Invalidate-and-refile: once superseded there is no current declaration, so a new one can be
    /// recorded — and the recording is a plain create again, not a special replace.
    /// </summary>
    [Fact]
    public async Task Supersedes_a_declaration_so_a_refiled_one_can_be_recorded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = StoreOn(await ConnectOrSkipAsync(cancellationToken));
        var id = AManifestId();
        await store.SaveAsync(ADeclaration(id), cancellationToken);

        var superseded = await store.SupersedeAsync(id, cancellationToken);
        var afterSupersede = await store.GetAsync(id, cancellationToken);
        var refiled = await store.SaveAsync(ADeclaration(id, "26GB99999999999ZZ9"), cancellationToken);

        superseded.Should().BeTrue();
        afterSupersede.Should().BeNull();
        refiled.Should().BeTrue();
        (await store.GetAsync(id, cancellationToken))!.Mrn.Should().Be("26GB99999999999ZZ9");
    }

    /// <summary>
    /// The superseded declaration is kept, not discarded. A customs query about a crossing months
    /// later is about the MRN that was filed at the time, which may well be one that was withdrawn.
    /// </summary>
    [Fact]
    public async Task Keeps_the_declaration_it_superseded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var blobs = await ConnectOrSkipAsync(cancellationToken);
        var store = StoreOn(blobs);
        var id = AManifestId();
        await store.SaveAsync(ADeclaration(id), cancellationToken);

        await store.SupersedeAsync(id, cancellationToken);

        var kept = blobs.GetBlobContainerClient(Container).GetBlobsAsync(
            BlobTraits.None, BlobStates.None, $"{id}/", cancellationToken);
        var names = new List<string>();

        await foreach (var blob in kept)
        {
            names.Add(blob.Name);
        }

        names.Should().ContainSingle().Which.Should().Contain(id).And.EndWith(".json");
    }

    [Fact]
    public async Task Reports_nothing_to_supersede_when_no_declaration_was_recorded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = StoreOn(await ConnectOrSkipAsync(cancellationToken));

        var superseded = await store.SupersedeAsync(AManifestId(), cancellationToken);

        superseded.Should().BeFalse();
    }
}

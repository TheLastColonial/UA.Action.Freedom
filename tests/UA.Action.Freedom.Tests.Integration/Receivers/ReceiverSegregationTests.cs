using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Data;
using UA.Action.Freedom.Data.Receivers;

namespace UA.Action.Freedom.Tests.Integration.Receivers;

/// <summary>
/// The receiver split, against a real database with real principals.
/// </summary>
/// <remarks>
/// The important test here is <see cref="The_application_identity_cannot_read_a_delivery_address"/>.
/// Everything else in the codebase enforces §4.4 in C#: a policy, a separate port, a separate
/// connection factory. All of that is code, and code can be changed by someone who does not know
/// why it is there. The <c>DENY</c> on the <c>sensitive</c> schema is the control that survives
/// that, and this is the test that proves it is switched on rather than merely written down.
///
/// It only proves anything because the application connects as an ordinary login. <c>sa</c> is
/// sysadmin and bypasses permission checks entirely, so running these as <c>sa</c> would make
/// the whole file pass while enforcing nothing.
/// </remarks>
[Trait("Category", "Integration")]
public class ReceiverSegregationTests
{
    private const string DefaultAppConnectionString =
        "Server=localhost,1433;Database=Freedom;User Id=freedom_app;Password=Local_Freedom_App_1;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

    private const string DefaultSensitiveConnectionString =
        "Server=localhost,1433;Database=Freedom;User Id=freedom_sensitive;Password=Local_Freedom_Sensitive_1;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

    private static string AppConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Freedom") ?? DefaultAppConnectionString;

    private static string SensitiveConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__FreedomSensitive") ?? DefaultSensitiveConnectionString;

    private static async Task SkipUnlessReachableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(AppConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM dbo.Receiver";
            await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            Assert.Skip($"Freedom database with dbo.Receiver is not reachable as freedom_app: {exception.Message}");
        }
    }

    private static ReceiverRepository AppRepository() =>
        new(new SqlConnectionFactory(Configuration(AppConnectionString, SensitiveConnectionString)));

    private static ReceiverDetailRepository GroundOfficerRepository() =>
        new(new SensitiveSqlConnectionFactory(Configuration(AppConnectionString, SensitiveConnectionString)));

    private static IConfiguration Configuration(string app, string sensitive) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Freedom"] = app,
                ["ConnectionStrings:FreedomSensitive"] = sensitive,
            })
            .Build();

    private static ReceiverReadModel AReceiver(Guid receiverRef) =>
        new(receiverRef, "Kharkiv Regional Hospital", "Kharkiv oblast");

    private static ReceiverDetailReadModel ADetail(Guid receiverRef) => new(
        receiverRef, "Olena Kovalenko", "+380501234567", "12 Vulytsia Sumska", null, "Kharkiv", "61002", null);

    private static async Task RemoveAsync(Guid receiverRef)
    {
        // As the Ground Officer identity: the application's own cannot touch the detail table,
        // which is the point of the whole arrangement.
        await using var sensitive = new SqlConnection(SensitiveConnectionString);
        await sensitive.OpenAsync();
        await using var detail = sensitive.CreateCommand();
        detail.CommandText =
            "DELETE FROM sensitive.ReceiverDetail WHERE ReceiverRef = @r; "
            + "DELETE FROM sensitive.ReceiverDetailAccessLog WHERE ReceiverRef = @r;";
        detail.Parameters.AddWithValue("@r", receiverRef);
        await detail.ExecuteNonQueryAsync();

        await using var app = new SqlConnection(AppConnectionString);
        await app.OpenAsync();
        await using var receiver = app.CreateCommand();
        receiver.CommandText = "DELETE FROM dbo.Receiver WHERE ReceiverRef = @r";
        receiver.Parameters.AddWithValue("@r", receiverRef);
        await receiver.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task The_application_identity_cannot_read_a_delivery_address()
    {
        // The load-bearing line of iac/local/sql/001-schemas.sql, asserted rather than assumed.
        var cancellationToken = TestContext.Current.CancellationToken;
        await SkipUnlessReachableAsync(cancellationToken);

        await using var connection = new SqlConnection(AppConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM sensitive.ReceiverDetail";

        var read = async () => await command.ExecuteScalarAsync(cancellationToken);

        (await read.Should().ThrowAsync<SqlException>())
            .Which.Message.Should().Contain("permission was denied");
    }

    [Fact]
    public async Task The_ground_officer_identity_can()
    {
        // The mirror image, so the test above is known to be measuring a permission rather than
        // a missing table or a typo in the schema name.
        var cancellationToken = TestContext.Current.CancellationToken;
        await SkipUnlessReachableAsync(cancellationToken);

        await using var connection = new SqlConnection(SensitiveConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM sensitive.ReceiverDetail";

        var read = async () => await command.ExecuteScalarAsync(cancellationToken);

        await read.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Round_trips_a_receiver_through_the_application_identity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SkipUnlessReachableAsync(cancellationToken);
        var receiverRef = Guid.NewGuid();
        var repository = AppRepository();

        try
        {
            await repository.AddAsync(AReceiver(receiverRef), cancellationToken);

            var stored = await repository.GetByRefAsync(receiverRef, cancellationToken);

            stored.Should().Be(AReceiver(receiverRef));
        }
        finally
        {
            await RemoveAsync(receiverRef);
        }
    }

    [Fact]
    public async Task Resolving_an_address_writes_exactly_one_audit_row()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SkipUnlessReachableAsync(cancellationToken);
        var receiverRef = Guid.NewGuid();
        var receivers = AppRepository();
        var detail = GroundOfficerRepository();

        try
        {
            await receivers.AddAsync(AReceiver(receiverRef), cancellationToken);
            await detail.UpsertAsync(ADetail(receiverRef), cancellationToken);

            (await detail.CountAccessesAsync(receiverRef, cancellationToken)).Should().Be(0);

            var resolved = await detail.ResolveAsync(
                receiverRef, "ground-officer-1", "Delivery scheduled 12 Sept", cancellationToken);

            resolved!.AddressLine1.Should().Be("12 Vulytsia Sumska");

            // The audit row and the read commit together, so one resolve is one entry.
            (await detail.CountAccessesAsync(receiverRef, cancellationToken)).Should().Be(1);

            await detail.ResolveAsync(receiverRef, "ground-officer-1", null, cancellationToken);
            (await detail.CountAccessesAsync(receiverRef, cancellationToken)).Should().Be(2);
        }
        finally
        {
            await RemoveAsync(receiverRef);
        }
    }

    [Fact]
    public async Task An_attempt_to_resolve_an_address_that_is_not_there_is_still_audited()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SkipUnlessReachableAsync(cancellationToken);
        var receiverRef = Guid.NewGuid();
        var receivers = AppRepository();
        var detail = GroundOfficerRepository();

        try
        {
            await receivers.AddAsync(AReceiver(receiverRef), cancellationToken);

            var resolved = await detail.ResolveAsync(receiverRef, "ground-officer-1", null, cancellationToken);

            resolved.Should().BeNull();
            (await detail.CountAccessesAsync(receiverRef, cancellationToken)).Should().Be(1);
        }
        finally
        {
            await RemoveAsync(receiverRef);
        }
    }

    [Fact]
    public async Task The_audit_trail_outlives_the_address_it_describes()
    {
        // Retention removes the address (§4.4.5); the record of who read it is what remains.
        var cancellationToken = TestContext.Current.CancellationToken;
        await SkipUnlessReachableAsync(cancellationToken);
        var receiverRef = Guid.NewGuid();
        var receivers = AppRepository();
        var detail = GroundOfficerRepository();

        try
        {
            await receivers.AddAsync(AReceiver(receiverRef), cancellationToken);
            await detail.UpsertAsync(ADetail(receiverRef), cancellationToken);
            await detail.ResolveAsync(receiverRef, "ground-officer-1", null, cancellationToken);

            (await detail.DeleteAsync(receiverRef, cancellationToken)).Should().BeTrue();

            (await detail.ResolveAsync(receiverRef, "ground-officer-1", null, cancellationToken)).Should().BeNull();
            (await detail.CountAccessesAsync(receiverRef, cancellationToken)).Should().Be(2);
        }
        finally
        {
            await RemoveAsync(receiverRef);
        }
    }

    [Fact]
    public async Task Updating_delivery_detail_replaces_it_rather_than_adding_a_second_row()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SkipUnlessReachableAsync(cancellationToken);
        var receiverRef = Guid.NewGuid();
        var receivers = AppRepository();
        var detail = GroundOfficerRepository();

        try
        {
            await receivers.AddAsync(AReceiver(receiverRef), cancellationToken);
            await detail.UpsertAsync(ADetail(receiverRef), cancellationToken);
            await detail.UpsertAsync(ADetail(receiverRef) with { City = "Poltava", AddressLine1 = "4 Vulytsia Soborna" },
                cancellationToken);

            var resolved = await detail.ResolveAsync(receiverRef, "ground-officer-1", null, cancellationToken);

            resolved!.City.Should().Be("Poltava");
            resolved.AddressLine1.Should().Be("4 Vulytsia Soborna");
        }
        finally
        {
            await RemoveAsync(receiverRef);
        }
    }
}

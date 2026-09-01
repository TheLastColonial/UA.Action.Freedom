using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Data;
using UA.Action.Freedom.Data.Boxes;

namespace UA.Action.Freedom.Tests.Integration.Boxes;

/// <summary>
/// The Dapper <see cref="BoxRepository"/> QR-label methods against real <c>dbo.BoxQrCode</c>.
/// Skips itself when the local stack is not up.
/// </summary>
/// <remarks>
/// Three things need a real database here: that re-issuing revokes the old row and inserts the
/// new one <em>as one transaction</em>, so a box never has two live labels or none; that
/// <see cref="BoxRepository.ResolveActiveQrCodeAsync"/> ignores a revoked token; and that the
/// foreign key cascades labels away with the box they belong to.
/// </remarks>
[Trait("Category", "Integration")]
public class BoxQrCodeRepositoryTests
{
    private const string DefaultLocalConnectionString =
        "Server=localhost,1433;Database=Freedom;User Id=freedom_app;Password=Local_Freedom_App_1;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Freedom") ?? DefaultLocalConnectionString;

    private static async Task<BoxRepository> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM dbo.Box; SELECT COUNT(1) FROM dbo.BoxQrCode;";
            await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            Assert.Skip($"Freedom database with dbo.BoxQrCode is not reachable: {exception.Message}");
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Freedom"] = ConnectionString })
            .Build();

        return new BoxRepository(new SqlConnectionFactory(configuration));
    }

    private static BoxReadModel ANewBox() => new(
        Id: 0, WeightKg: 0, ReceiverRef: null,
        House: "Unit 4", Street: "Cross Road", City: "Coventry", Country: "United Kingdom", Postcode: "CV1 2AB",
        ValidatedByPersonId: null, ValidatedAt: null);

    private static Task RemoveBoxAsync(int id) =>
        ExecuteAsync("DELETE FROM dbo.Box WHERE Id = @id", ("@id", id));

    private static async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Issues_a_label_and_reads_it_back_as_the_active_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var boxId = await repository.AddAsync(ANewBox(), cancellationToken);

        try
        {
            var token = Guid.NewGuid();
            var issuedAt = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

            var issued = await repository.IssueQrCodeAsync(boxId, token, issuedAt, cancellationToken);
            issued.Token.Should().Be(token);
            issued.Active.Should().BeTrue();

            var active = await repository.GetActiveQrCodeAsync(boxId, cancellationToken);
            active.Should().NotBeNull();
            active!.Token.Should().Be(token);
            active.Active.Should().BeTrue();

            var resolved = await repository.ResolveActiveQrCodeAsync(token, cancellationToken);
            resolved!.BoxId.Should().Be(boxId);
        }
        finally
        {
            await RemoveBoxAsync(boxId);
        }
    }

    [Fact]
    public async Task Re_issuing_revokes_the_previous_row_in_one_transaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var boxId = await repository.AddAsync(ANewBox(), cancellationToken);

        try
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();

            await repository.IssueQrCodeAsync(boxId, first, DateTime.UtcNow, cancellationToken);
            await repository.IssueQrCodeAsync(boxId, second, DateTime.UtcNow, cancellationToken);

            var active = await repository.GetActiveQrCodeAsync(boxId, cancellationToken);
            active!.Token.Should().Be(second);

            (await ScalarAsync("SELECT COUNT(1) FROM dbo.BoxQrCode WHERE BoxId = @id", ("@id", boxId)))
                .Should().Be(2);
            (await ScalarAsync(
                "SELECT COUNT(1) FROM dbo.BoxQrCode WHERE BoxId = @id AND RevokedAt IS NULL", ("@id", boxId)))
                .Should().Be(1);

            // The replaced token no longer resolves.
            (await repository.ResolveActiveQrCodeAsync(first, cancellationToken)).Should().BeNull();
        }
        finally
        {
            await RemoveBoxAsync(boxId);
        }
    }

    [Fact]
    public async Task A_revoked_token_reads_as_unknown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var boxId = await repository.AddAsync(ANewBox(), cancellationToken);

        try
        {
            var token = Guid.NewGuid();
            await repository.IssueQrCodeAsync(boxId, token, DateTime.UtcNow, cancellationToken);

            (await repository.RevokeActiveQrCodeAsync(boxId, cancellationToken)).Should().BeTrue();

            (await repository.ResolveActiveQrCodeAsync(token, cancellationToken)).Should().BeNull();
            (await repository.GetActiveQrCodeAsync(boxId, cancellationToken)).Should().BeNull();
        }
        finally
        {
            await RemoveBoxAsync(boxId);
        }
    }

    [Fact]
    public async Task Revoking_when_nothing_is_active_returns_false()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var boxId = await repository.AddAsync(ANewBox(), cancellationToken);

        try
        {
            (await repository.RevokeActiveQrCodeAsync(boxId, cancellationToken)).Should().BeFalse();
        }
        finally
        {
            await RemoveBoxAsync(boxId);
        }
    }

    [Fact]
    public async Task Deleting_the_box_takes_its_qr_codes_with_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var boxId = await repository.AddAsync(ANewBox(), cancellationToken);

        await repository.IssueQrCodeAsync(boxId, Guid.NewGuid(), DateTime.UtcNow, cancellationToken);
        await repository.IssueQrCodeAsync(boxId, Guid.NewGuid(), DateTime.UtcNow, cancellationToken);

        (await repository.DeleteAsync(boxId, cancellationToken)).Should().BeTrue();

        // The cascade is what stops a stray label outliving the box it named.
        (await ScalarAsync("SELECT COUNT(1) FROM dbo.BoxQrCode WHERE BoxId = @id", ("@id", boxId)))
            .Should().Be(0);
    }
}

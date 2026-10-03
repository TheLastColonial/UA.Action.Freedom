using Dapper;
using Microsoft.Data.SqlClient;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Categories;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Data.Categories;

/// <summary>
/// Dapper-backed <see cref="IItemCategoryRepository"/> over <c>dbo.ItemCategory</c> and
/// <c>dbo.CategoryCustomsCode</c>.
/// </summary>
public sealed class ItemCategoryRepository(IDbConnectionFactory connectionFactory, IChangeAttribution attribution)
    : IItemCategoryRepository
{
    private static string CodeColumn(CustomsAuthority authority, string alias) =>
        $"(SELECT cc.Code FROM dbo.CategoryCustomsCode AS cc WHERE cc.CategoryId = c.Id AND cc.Authority = {(int)authority}) AS {alias}";

    private static readonly string Columns =
        "c.Id, c.NameEn, c.NameUk, c.IsFixed, c.HazardClass, c.IsSensitive, c.IsNotCarried, c.WarnWithinDays, "
        + $"{CodeColumn(CustomsAuthority.UK, "UkCode")}, {CodeColumn(CustomsAuthority.EU, "EuCode")}, "
        + $"{CodeColumn(CustomsAuthority.UA, "UaCode")}, {ChangeStamp.ReadColumns("c")}";

    private static readonly string From = $"dbo.ItemCategory AS c {ChangeStamp.ReadJoin("c")}";

    public async Task<ItemCategoryReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<ItemCategoryReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM {From} WHERE c.Id = @id",
            new { id },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ItemCategoryReadModel>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<ItemCategoryReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM {From} ORDER BY c.IsFixed DESC, c.NameEn",
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<int?> AddAsync(ItemCategoryReadModel category, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        try
        {
            // IsFixed is not insertable: only the seeded list is built in.
            return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                INSERT INTO dbo.ItemCategory
                    (NameEn, NameUk, HazardClass, IsSensitive, IsNotCarried, WarnWithinDays, LastChangedBy, LastChangedAt)
                VALUES
                    (@NameEn, @NameUk, @HazardClass, @IsSensitive, @IsNotCarried, @WarnWithinDays, @changedBy, SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """,
                attribution.With(category),
                cancellationToken: cancellationToken));
        }
        catch (SqlException exception) when (IsDuplicate(exception))
        {
            return null;
        }
    }

    public async Task<bool?> UpdateAsync(ItemCategoryReadModel category, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        try
        {
            var affected = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ItemCategory SET
                    NameEn = @NameEn,
                    NameUk = @NameUk,
                    HazardClass = @HazardClass,
                    IsSensitive = @IsSensitive,
                    IsNotCarried = @IsNotCarried,
                    WarnWithinDays = @WarnWithinDays,
                    LastChangedBy = @changedBy,
                    LastChangedAt = SYSUTCDATETIME()
                WHERE Id = @Id
                """,
                attribution.With(category),
                cancellationToken: cancellationToken));

            return affected > 0;
        }
        catch (SqlException exception) when (IsDuplicate(exception))
        {
            return null;
        }
    }

    public async Task<bool> SetCodeAsync(
        int id, CustomsAuthority authority, string? code, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        // Two statements that are one fact, scoped to one category: the mapping row and the stamp on the
        // category it belongs to. A category the mapping names must exist, so absence is checked first.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.ItemCategory WITH (UPDLOCK) WHERE Id = @id",
            new { id },
            transaction,
            cancellationToken: cancellationToken));

        if (exists == 0)
        {
            return false;
        }

        var parameters = attribution.With(new { id, authority = (int)authority, code });

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.CategoryCustomsCode WHERE CategoryId = @id AND Authority = @authority",
            parameters,
            transaction,
            cancellationToken: cancellationToken));

        if (code is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.CategoryCustomsCode (CategoryId, Authority, Code, LastChangedBy, LastChangedAt)
                VALUES (@id, @authority, @code, @changedBy, SYSUTCDATETIME())
                """,
                parameters,
                transaction,
                cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.ItemCategory SET LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME() WHERE Id = @id",
            parameters,
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static bool IsDuplicate(SqlException exception) =>
        exception.Number is SqlErrors.UniqueConstraintViolation or SqlErrors.UniqueIndexViolation;
}

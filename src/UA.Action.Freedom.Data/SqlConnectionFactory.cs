using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace UA.Action.Freedom.Data;

/// <summary>
/// Builds <see cref="SqlConnection"/>s from the <c>Freedom</c> connection string — the same
/// key <c>DatabaseHealthCheck</c> reads. Locally that string carries SQL auth; in Azure it
/// carries <c>Authentication=Active Directory Default</c> and no secret, so only the string
/// differs between environments, not this code.
/// </summary>
public sealed class SqlConnectionFactory(IConfiguration configuration) : IDbConnectionFactory
{
    public const string ConnectionStringName = "Freedom";

    public DbConnection Create()
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"No '{ConnectionStringName}' connection string is configured.");
        }

        return new SqlConnection(connectionString);
    }
}

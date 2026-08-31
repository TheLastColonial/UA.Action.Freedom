using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace UA.Action.Freedom.Data;

/// <summary>
/// Hands out connections that can read the <c>sensitive</c> schema — Ukrainian delivery
/// addresses and receiver contacts.
/// </summary>
/// <remarks>
/// A separate interface from <see cref="IDbConnectionFactory"/> rather than a named lookup on
/// it, so the boundary is in the type system: a repository that asks for
/// <see cref="IDbConnectionFactory"/> <em>cannot</em> be handed the Ground Officer connection
/// by mistake, and the sensitive repository cannot silently fall back to the application's own.
/// Widening access then requires changing a constructor signature, which is the kind of change
/// a reviewer notices (docs/recommendations.md §4.4).
/// </remarks>
public interface ISensitiveDbConnectionFactory
{
    DbConnection Create();
}

/// <summary>
/// Builds connections from the <c>FreedomSensitive</c> connection string — a principal in the
/// <c>ground_officer</c> database role, the only role granted SELECT on <c>sensitive</c>.
/// </summary>
/// <remarks>
/// Locally that is the <c>freedom_sensitive</c> SQL login; in Azure it is a second managed
/// identity with Entra-only authentication and no secret in the string at all (§4.2). Only the
/// string differs between environments, not this code.
/// </remarks>
public sealed class SensitiveSqlConnectionFactory(IConfiguration configuration) : ISensitiveDbConnectionFactory
{
    public const string ConnectionStringName = "FreedomSensitive";

    public DbConnection Create()
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Deliberately not falling back to the application connection. That would look like
            // it worked, right up until it silently could not read anything — or, worse, could.
            throw new InvalidOperationException(
                $"No '{ConnectionStringName}' connection string is configured. Resolving a receiver's "
                + "delivery address needs the Ground Officer database identity.");
        }

        return new SqlConnection(connectionString);
    }
}

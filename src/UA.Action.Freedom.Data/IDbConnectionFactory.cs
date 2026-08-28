using System.Data.Common;

namespace UA.Action.Freedom.Data;

/// <summary>
/// Hands out connections to the <c>Freedom</c> database. Connections come back closed;
/// Dapper opens and closes them per command.
/// </summary>
public interface IDbConnectionFactory
{
    DbConnection Create();
}

using Dapper;

namespace UA.Action.Freedom.Data;

/// <summary>
/// A <c>varchar(32)</c> natural key — a VIN or a manifest reference — as a query parameter.
/// </summary>
/// <remarks>
/// Dapper sends a .NET string as <c>nvarchar(4000)</c>. Under the database's SQL collation
/// (<c>SQL_Latin1_General_CP1_CI_AS</c>, also Azure SQL's default) comparing a <c>varchar</c>
/// column with an <c>nvarchar</c> parameter converts the <em>column</em>, so <c>WHERE Vin = @vin</c>
/// scans the table instead of seeking the key. Beyond the cost, a scan locks every row it
/// reads: single-row writes then collide with any transaction touching other vehicles, and the
/// convoy-arrival transaction deadlocked against inspection updates until this was fixed.
/// Where a whole record is the parameter object, the SQL casts instead:
/// <c>CAST(@Vin AS varchar(32))</c> converts the parameter, not the column.
/// </remarks>
internal static class SqlKey
{
    private const int Length = 32;

    public static DbString Of(string value) => new() { Value = value, IsAnsi = true, Length = Length };
}

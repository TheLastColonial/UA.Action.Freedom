using System.Data;
using Dapper;
using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Data;

/// <summary>
/// What every repository shares to record and show who last changed a row. <see cref="With"/> adds the
/// <c>@changedBy</c> parameter every write statement uses.
/// The statements spell the assignments out, <c>LastChangedBy = @changedBy, LastChangedAt =
/// SYSUTCDATETIME()</c>, so the stamp is visible in the same statement as the change.
/// </summary>
internal static class ChangeStamp
{
    /// <summary>For a read: the two columns every read model carries, given the entity's alias.</summary>
    public static string ReadColumns(string alias) =>
        $"changer.DisplayName AS LastChangedByName, {alias}.LastChangedAt";

    /// <summary>For a read: the join <see cref="ReadColumns"/> needs. An erased volunteer reads "Former volunteer".</summary>
    public static string ReadJoin(string alias) =>
        $"LEFT JOIN dbo.PersonDisplay AS changer ON changer.PersonId = {alias}.LastChangedBy";

    public static DynamicParameters With(this IChangeAttribution attribution, object? parameters = null)
    {
        var all = new DynamicParameters(parameters);
        all.Add("changedBy", attribution.PersonId, DbType.Guid);
        return all;
    }
}

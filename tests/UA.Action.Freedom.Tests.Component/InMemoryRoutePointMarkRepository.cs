using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Standing in for <c>dbo.RoutePointMark</c>. It enforces what the SQL does: one mark per point, kind and vehicle (a
/// whole-convoy mark and a vehicle's are different rows), a crossing closes the accepted declarations of the kinds given
/// for the vehicles still travelling (or just the named one) in the same step, and a marked route point is one the route
/// cannot lose.
/// </summary>
internal sealed class InMemoryRoutePointMarkRepository(
    InMemoryConvoyRepository convoys, InMemoryDeclarationRepository declarations)
    : IRoutePointMarkRepository, IRecordsWhoChanged
{
    private readonly ChangeLedger<int> changes = new();
    private readonly List<RoutePointMark> marks = [];

    public void Attach(IChangeAttribution attribution, IPersonRepository people) => changes.Attach(attribution, people);

    public Task<IReadOnlyList<RoutePointMarkReadModel>> ListAsync(int convoyId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RoutePointMarkReadModel>>(
        [
            .. marks
                .Select((mark, index) => (mark, index))
                .Where(entry => entry.mark.ConvoyId == convoyId)
                .Select(entry => new RoutePointMarkReadModel(
                    entry.mark.RoutePointId,
                    entry.mark.Kind,
                    entry.mark.Vin,
                    changes.Of(entry.index).Name,
                    entry.mark.RecordedAt,
                    entry.mark.OnBehalfOfLeader)),
        ]);

    public Task<RecordMarkResult> RecordAsync(
        RoutePointMark mark, IReadOnlyList<DeclarationKind> closes, CancellationToken cancellationToken)
    {
        var already = marks.Exists(existing =>
            existing.ConvoyId == mark.ConvoyId
            && existing.RoutePointId == mark.RoutePointId
            && existing.Kind == mark.Kind
            && string.Equals(existing.Vin, mark.Vin, StringComparison.OrdinalIgnoreCase));

        if (already)
        {
            return Task.FromResult(RecordMarkResult.AlreadyMarked);
        }

        marks.Add(mark);
        changes.Stamp(marks.Count - 1);
        convoys.ReferenceRoutePoint(mark.RoutePointId);

        if (closes.Count > 0)
        {
            declarations.CloseAccepted(mark.ConvoyId, closes, mark.Vin);
        }

        return Task.FromResult(RecordMarkResult.Recorded);
    }
}

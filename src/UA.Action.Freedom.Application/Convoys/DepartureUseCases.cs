using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>Set the convoy off: the one departure action (O36).</summary>
public sealed record DepartConvoyCommand(int ConvoyId);

public enum DepartConvoyOutcome
{
    Departed,
    NotFound,
    AlreadyDeparted,
    NotReady
}

/// <param name="Unmet">When refused as not ready, every blocking requirement still outstanding.</param>
public sealed record DepartConvoyResult(DepartConvoyOutcome Outcome, IReadOnlyList<Requirement> Unmet)
{
    public static DepartConvoyResult Of(DepartConvoyOutcome outcome) => new(outcome, []);
}

/// <summary>
/// A convoy departs when no blocking requirement is outstanding (ADR 0008). There is no override, no waiver and no
/// "depart anyway": the rule is the rule, and where a real need exists the rule itself changes (O3).
/// </summary>
/// <remarks>
/// It asks the same query that serves <c>GET /convoys/{id}/readiness</c>, so what a Dispatcher saw is what is enforced.
/// <para>
/// It touches no manifest: the sign-off is the Administrator's, and a convoy departs on its requirements, not on where
/// a manifest has got to.
/// </para>
/// </remarks>
public sealed class DepartConvoyHandler(
    IQueryHandler<GetConvoyReadinessQuery, ConvoyReadinessReadModel?> readiness,
    IConvoyRepository convoys,
    TimeProvider clock)
    : ICommandHandler<DepartConvoyCommand, DepartConvoyResult>
{
    public async Task<DepartConvoyResult> HandleAsync(DepartConvoyCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(command.ConvoyId, cancellationToken);

        if (convoy is null)
        {
            return DepartConvoyResult.Of(DepartConvoyOutcome.NotFound);
        }

        if (convoy.Departed || convoy.Arrived)
        {
            return DepartConvoyResult.Of(DepartConvoyOutcome.AlreadyDeparted);
        }

        var assessment = await readiness.HandleAsync(new GetConvoyReadinessQuery(command.ConvoyId), cancellationToken);
        var unmet = assessment?.Requirements
            .Where(requirement => requirement.Severity == RequirementSeverity.Blocking && requirement.State != RequirementState.Done)
            .ToList() ?? [];

        if (unmet.Count > 0)
        {
            return new DepartConvoyResult(DepartConvoyOutcome.NotReady, unmet);
        }

        if (!await convoys.DepartAsync(command.ConvoyId, clock.GetUtcNow().UtcDateTime, cancellationToken))
        {
            return DepartConvoyResult.Of(DepartConvoyOutcome.AlreadyDeparted);
        }

        return DepartConvoyResult.Of(DepartConvoyOutcome.Departed);
    }
}

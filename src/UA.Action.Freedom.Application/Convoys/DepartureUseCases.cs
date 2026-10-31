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
/// Transitional, until plan 15: each vehicle still travelling has its manifest moved to <c>InTransit</c> when it is
/// <c>Ready</c>, so the legacy delivered / arrival flow keeps working. A manifest in any other state is left alone and
/// does not hold the convoy back, because the manifest is no longer what decides readiness.
/// </para>
/// </remarks>
public sealed class DepartConvoyHandler(
    IQueryHandler<GetConvoyReadinessQuery, ConvoyReadinessReadModel?> readiness,
    IConvoyRepository convoys,
    IConvoyVehicleRepository truckList,
    IManifestRepository manifests,
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

        await SetManifestsInTransitAsync(command.ConvoyId, cancellationToken);

        return DepartConvoyResult.Of(DepartConvoyOutcome.Departed);
    }

    private async Task SetManifestsInTransitAsync(int convoyId, CancellationToken cancellationToken)
    {
        foreach (var vehicle in (await truckList.ListAsync(convoyId, cancellationToken)).Where(entry => entry.Travelling))
        {
            var manifest = await manifests.GetForVehicleAsync(convoyId, vehicle.Vin, cancellationToken);

            if (manifest is not null && ManifestTransitions.CanTransition(manifest.Status, ManifestStatus.InTransit))
            {
                await manifests.TransitionAsync(manifest.Id, manifest.Status, ManifestStatus.InTransit, cancellationToken);
            }
        }
    }
}

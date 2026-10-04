using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Declarations;

/// <summary>What the Dispatcher does about a stale declaration; it depends on the instrument.</summary>
public enum RedeclareResolution
{
    /// <summary>GMR: update it, or delete and recreate. Manual until the GVMS client is wired in.</summary>
    UpdateOrRecreate,

    /// <summary>ENS: invalidate and refile. A new MRN is recorded and the old one is kept as history.</summary>
    InvalidateAndRefile,

    /// <summary>ELO: a new envelope against the new MRN.</summary>
    NewEnvelopeAgainstNewMrn,

    /// <summary>Goods list: prepare a new one for the Receiver to file; the goods wait at a registered hub (D24, D31).</summary>
    PrepareNewListAndHoldAtHub,
}

/// <summary>
/// An immediate re-declare task (D13, O21): shown on screen, never emailed. It is derived from the
/// stale declarations, so there is nothing to close: resolving the declaration is what clears it.
/// Carries identifiers only, so it has nowhere to put an address or an authority's text.
/// </summary>
public sealed record RedeclareTaskReadModel(
    int DeclarationId,
    string Vin,
    DeclarationKind Kind,
    Guid? ReceiverRef,
    string? Reference,
    RedeclareResolution Resolution);

public static class RedeclareResolutions
{
    public static RedeclareResolution For(DeclarationKind kind) => kind switch
    {
        DeclarationKind.Gmr => RedeclareResolution.UpdateOrRecreate,
        DeclarationKind.Ens => RedeclareResolution.InvalidateAndRefile,
        DeclarationKind.Elo => RedeclareResolution.NewEnvelopeAgainstNewMrn,
        _ => RedeclareResolution.PrepareNewListAndHoldAtHub,
    };
}

/// <summary>Every stale declaration on a convoy, or null when the convoy does not exist.</summary>
public sealed record ListRedeclareTasksQuery(int ConvoyId);

public sealed class ListRedeclareTasksHandler(
    IConvoyRepository convoys,
    IConvoyVehicleRepository truckList,
    IDeclarationRepository declarations,
    IVehicleLoadReader loads)
    : IQueryHandler<ListRedeclareTasksQuery, IReadOnlyList<RedeclareTaskReadModel>?>
{
    public async Task<IReadOnlyList<RedeclareTaskReadModel>?> HandleAsync(
        ListRedeclareTasksQuery query, CancellationToken cancellationToken)
    {
        if (await convoys.GetByIdAsync(query.ConvoyId, cancellationToken) is null)
        {
            return null;
        }

        var tasks = new List<RedeclareTaskReadModel>();

        foreach (var vehicle in await truckList.ListAsync(query.ConvoyId, cancellationToken))
        {
            var current = await loads.ReadAsync(query.ConvoyId, vehicle.Vin, cancellationToken);

            tasks.AddRange(
                (await declarations.ListAsync(query.ConvoyId, vehicle.Vin, cancellationToken))
                .Where(declaration => DeclarationStaleness.IsStale(declaration, current))
                .Select(declaration => new RedeclareTaskReadModel(
                    declaration.Id, declaration.Vin, declaration.Kind, declaration.ReceiverRef, declaration.Reference,
                    RedeclareResolutions.For(declaration.Kind))));
        }

        return tasks;
    }
}

/// <summary>Withdraw a stale declaration, keeping it and its reference, and start a new draft for the same scope.</summary>
public sealed record WithdrawDeclarationCommand(int ConvoyId, string Vin, int DeclarationId);

public enum WithdrawDeclarationOutcome
{
    Withdrawn,
    NotFound,
    NotStale,
}

public sealed class WithdrawDeclarationHandler(
    IConvoyVehicleRepository truckList, IDeclarationRepository declarations, IVehicleLoadReader loads)
    : ICommandHandler<WithdrawDeclarationCommand, WithdrawDeclarationOutcome>
{
    public async Task<WithdrawDeclarationOutcome> HandleAsync(
        WithdrawDeclarationCommand command, CancellationToken cancellationToken)
    {
        if (await truckList.GetAsync(command.ConvoyId, command.Vin, cancellationToken) is null)
        {
            return WithdrawDeclarationOutcome.NotFound;
        }

        var declaration = (await declarations.ListAsync(command.ConvoyId, command.Vin, cancellationToken))
            .FirstOrDefault(row => row.Id == command.DeclarationId);

        if (declaration is null)
        {
            return WithdrawDeclarationOutcome.NotFound;
        }

        var current = await loads.ReadAsync(command.ConvoyId, command.Vin, cancellationToken);

        if (!DeclarationStaleness.IsStale(declaration, current))
        {
            return WithdrawDeclarationOutcome.NotStale;
        }

        return await declarations.WithdrawAndRedraftAsync(
            command.ConvoyId, command.Vin, declaration.Kind, declaration.ReceiverRef, cancellationToken)
            ? WithdrawDeclarationOutcome.Withdrawn
            : WithdrawDeclarationOutcome.NotStale;
    }
}

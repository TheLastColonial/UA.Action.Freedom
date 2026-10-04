using System.Text.Json;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Declarations;

/// <summary>The load a vehicle is carrying now, in the shape a declaration is compared against.</summary>
public interface IVehicleLoadReader
{
    /// <summary>The vehicle's current load, or null when it is not on the convoy.</summary>
    Task<LoadSnapshot?> ReadAsync(int convoyId, string vin, CancellationToken cancellationToken);
}

/// <summary>
/// Composes the load from the ports that already own its parts: the truck-list entry, the boxes
/// allocated to it, their items and each box's Receiver status. About ten vehicles to a convoy, so it
/// is read fresh every time and never cached (plan 09).
/// </summary>
public sealed class VehicleLoadReader(
    IConvoyVehicleRepository truckList, IBoxRepository boxes, IReceiverRepository receivers) : IVehicleLoadReader
{
    public async Task<LoadSnapshot?> ReadAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        var entry = await truckList.GetAsync(convoyId, vin, cancellationToken);

        if (entry is null)
        {
            return null;
        }

        var cargo = await truckList.ListBoxesAsync(convoyId, vin, cancellationToken) ?? [];
        var registered = new Dictionary<Guid, bool>();
        var loaded = new List<LoadBox>();

        foreach (var allocated in cargo)
        {
            var box = await boxes.GetByIdAsync(allocated.BoxId, cancellationToken);
            var items = await boxes.ListItemsAsync(allocated.BoxId, cancellationToken);

            if (box is null)
            {
                continue;
            }

            loaded.Add(new LoadBox(
                box.Id,
                box.WeightKg,
                box.ReceiverRef,
                box.ReceiverRef is { } receiverRef && await IsRegisteredAsync(receiverRef, registered, cancellationToken),
                [.. items.Select(item => new LoadItem(item.Id, item.CategoryId, item.Quantity, item.ValueGbp, item.CommodityCode))]));
        }

        return new LoadSnapshot(LoadSnapshot.CurrentVersion, entry.Vin, entry.Withdrawn, loaded);
    }

    private async Task<bool> IsRegisteredAsync(
        Guid receiverRef, Dictionary<Guid, bool> seen, CancellationToken cancellationToken)
    {
        if (seen.TryGetValue(receiverRef, out var known))
        {
            return known;
        }

        var receiver = await receivers.GetByRefAsync(receiverRef, cancellationToken);
        return seen[receiverRef] = receiver is not null && ReceiverRegistration.CanReceive(receiver.Status);
    }
}

/// <summary>
/// How a snapshot is stored in <c>dbo.Declaration.SnapshotJson</c>. Unknown properties are ignored on
/// read and missing ones take their defaults, so a snapshot written by an older or a newer version of
/// the code still reads.
/// </summary>
public static class LoadSnapshotJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Write(LoadSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    public static LoadSnapshot? Read(string? json, int? version) =>
        json is null || version is null
            ? null
            : JsonSerializer.Deserialize<LoadSnapshot>(json, Options) is { } snapshot
                ? snapshot with { Version = version.Value }
                : null;
}

/// <summary>Reads the current load and stores it against a declaration that has none yet.</summary>
public interface IDeclarationSnapshots
{
    Task StampAsync(
        int convoyId, string vin, DeclarationKind kind, Guid? receiverRef, CancellationToken cancellationToken);
}

/// <summary>
/// For a declaration that went straight from draft to filed (a reference recorded by hand, or an
/// automatic filing) without being marked ready: it is still stamped with the load as it stood when it
/// was filed, so it can go stale like any other. A declaration that already has a snapshot keeps it.
/// </summary>
public sealed class DeclarationSnapshots(IDeclarationRepository declarations, IVehicleLoadReader loads)
    : IDeclarationSnapshots
{
    public async Task StampAsync(
        int convoyId, string vin, DeclarationKind kind, Guid? receiverRef, CancellationToken cancellationToken)
    {
        if (await loads.ReadAsync(convoyId, vin, cancellationToken) is not { } load)
        {
            return;
        }

        await declarations.StoreSnapshotAsync(
            convoyId, vin, kind, receiverRef, LoadSnapshotJson.Write(load), load.Version, cancellationToken);
    }
}

/// <summary>Mark a draft declaration ready to file, storing the load it was written from.</summary>
public sealed record MarkDeclarationReadyCommand(int ConvoyId, string Vin, DeclarationKind Kind, Guid? ReceiverRef = null);

public enum MarkDeclarationReadyOutcome
{
    Ready,
    VehicleNotOnConvoy,
    NotPreparable,
    ReceiverRequired,
    ReceiverNotAllowed,
}

public sealed class MarkDeclarationReadyHandler(IDeclarationRepository declarations, IVehicleLoadReader loads)
    : ICommandHandler<MarkDeclarationReadyCommand, MarkDeclarationReadyOutcome>
{
    public async Task<MarkDeclarationReadyOutcome> HandleAsync(
        MarkDeclarationReadyCommand command, CancellationToken cancellationToken)
    {
        if (command.Kind == DeclarationKind.GoodsList && command.ReceiverRef is null)
        {
            return MarkDeclarationReadyOutcome.ReceiverRequired;
        }

        if (command.Kind != DeclarationKind.GoodsList && command.ReceiverRef is not null)
        {
            return MarkDeclarationReadyOutcome.ReceiverNotAllowed;
        }

        if (await loads.ReadAsync(command.ConvoyId, command.Vin, cancellationToken) is not { } load)
        {
            return MarkDeclarationReadyOutcome.VehicleNotOnConvoy;
        }

        return await declarations.MarkReadyAsync(
                command.ConvoyId, command.Vin, command.Kind, command.ReceiverRef,
                LoadSnapshotJson.Write(load), load.Version, cancellationToken)
            switch
            {
                MarkReadyResult.Ready => MarkDeclarationReadyOutcome.Ready,
                MarkReadyResult.VehicleNotOnConvoy => MarkDeclarationReadyOutcome.VehicleNotOnConvoy,
                _ => MarkDeclarationReadyOutcome.NotPreparable,
            };
    }
}

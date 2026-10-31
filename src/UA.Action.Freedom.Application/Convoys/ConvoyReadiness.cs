using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Convoys;

public enum RequirementScope
{
    Convoy,
    Vehicle
}

/// <summary><see cref="Blocked"/> is waiting on another requirement; <see cref="Warning"/> is an advisory that applies.</summary>
public enum RequirementState
{
    Done,
    ToDo,
    Blocked,
    Warning
}

public enum RequirementSeverity
{
    Blocking,
    Advisory
}

/// <summary>
/// One thing about a convoy that is, or is not, as it should be (ADR 0008). Computed on every read and never stored.
/// <see cref="Code"/> is a stable name the web and the tests depend on: treat it as part of the API.
/// </summary>
/// <param name="Vin">The vehicle it is about, for a vehicle-scope requirement.</param>
/// <param name="ResolveLink">Where in the operator UI this is fixed.</param>
public sealed record Requirement(
    string Code,
    RequirementScope Scope,
    string? Vin,
    RequirementState State,
    string OwnerRole,
    RequirementSeverity Severity,
    string Message,
    string ResolveLink);

/// <summary>A box on the vehicle, as far as readiness cares: is it vouched for, and is its Receiver one it may be sent to.</summary>
public sealed record CargoBoxFact(int BoxId, bool Validated, bool ReceiverRegistered, Guid? ReceiverRef);

/// <summary>A declaration as it stands now: <see cref="Status"/> already has staleness derived into it.</summary>
public sealed record DeclarationFact(DeclarationKind Kind, Guid? ReceiverRef, DeclarationStatus Status);

/// <summary>Counts over a vehicle's allocated items that the advisory warnings are about.</summary>
public sealed record VehicleItemFacts(int WithoutValue, int WithoutDonor, int Expired, int ShortShelfLife)
{
    public static VehicleItemFacts None { get; } = new(0, 0, 0, 0);
}

public sealed record AccommodationFacts(bool AllCovered, int MissingCount, int LeftoverBookings)
{
    public static AccommodationFacts Covered { get; } = new(true, 0, 0);
}

public sealed record VehicleReadinessFacts(
    ConvoyVehicleReadModel Vehicle,
    VehicleInsuranceReadModel? Policy,
    FerryBookingReadModel? Ferry,
    bool HandoverReceiverRegistered,
    IReadOnlyList<CargoBoxFact> Boxes,
    IReadOnlyList<DeclarationFact> Declarations,
    VehicleItemFacts Items);

/// <summary>Everything the readiness rules need, gathered once. The rules themselves read nothing.</summary>
public sealed record ConvoyReadinessFacts(
    ConvoyReadModel Convoy,
    bool RoutePlanned,
    bool HasLeader,
    AccommodationFacts Accommodation,
    IReadOnlyList<VehicleReadinessFacts> Vehicles,
    IReadOnlyList<string> BudgetAdvisories);

/// <summary>
/// The readiness rules as one pure function (ADR 0008): requirements, blocking and advisory. Every date rule is judged on
/// the single <c>asOf</c> the caller supplies, so a readiness read and a departure can never disagree about insurance.
/// </summary>
/// <remarks>
/// A withdrawn vehicle is skipped entirely. It broke down and left: it has no crew to find and no insurance to renew,
/// and listing it would keep the convoy permanently not ready for something nobody can fix.
/// </remarks>
public static class ConvoyReadiness
{
    public const int DriversRequired = 1;

    public const int DriversAdvised = 2;

    public static ConvoyReadinessReadModel Assess(ConvoyReadinessFacts facts, DateTime asOf)
    {
        var convoyId = facts.Convoy.Id;
        var travelling = facts.Vehicles.Where(entry => entry.Vehicle.Travelling).ToList();

        var perVehicle = travelling.Select(entry => (entry.Vehicle, Requirements: VehicleRequirements(convoyId, entry, facts.Convoy, asOf))).ToList();

        Requirement[] convoyRequirements =
        [
            Blocking("route", facts.RoutePlanned, "Dispatcher", "No route planned", $"/convoys/{convoyId}/route"),
            Blocking("vehicles", travelling.Count > 0, "Dispatcher", "No vehicles on the truck list", $"/convoys/{convoyId}/vehicles"),
            Blocking("leader", facts.HasLeader, "Dispatcher", "No Convoy Leader assigned", $"/convoys/{convoyId}/leader"),
            Blocking(
                "accommodation",
                facts.Accommodation.AllCovered,
                "Dispatcher",
                "Accommodation missing at an overnight stop",
                $"/convoys/{convoyId}/accommodation"),
            .. facts.Accommodation.LeftoverBookings > 0
                ? new[] { Advisory("advisory.leftover-booking", null, "Dispatcher", "A booking covers nobody still crewed", $"/convoys/{convoyId}/accommodation") }
                : [],
            .. facts.BudgetAdvisories.Count > 0
                ? new[] { Advisory("advisory.budget", null, "Dispatcher", string.Join("; ", facts.BudgetAdvisories), $"/convoys/{convoyId}/budget") }
                : [],
        ];

        var notReady = perVehicle.Count(entry => entry.Requirements.Any(IsOutstandingBlocker));
        var convoyReasons = convoyRequirements.Where(IsOutstandingBlocker).Select(requirement => requirement.Message).ToList();

        if (notReady > 0)
        {
            convoyReasons.Add(notReady == 1 ? "1 vehicle not ready" : $"{notReady} vehicles not ready");
        }

        var requirements = convoyRequirements.Concat(perVehicle.SelectMany(entry => entry.Requirements)).ToList();

        return new ConvoyReadinessReadModel(
            convoyReasons.Count == 0,
            facts.RoutePlanned,
            convoyReasons,
            [.. perVehicle.Select(entry => VehicleSummary(entry.Vehicle, entry.Requirements))])
        {
            Advisories = facts.BudgetAdvisories,
            Requirements = requirements,
        };
    }

    private static bool IsOutstandingBlocker(Requirement requirement) =>
        requirement.Severity == RequirementSeverity.Blocking && requirement.State != RequirementState.Done;

    private static VehicleReadinessReadModel VehicleSummary(ConvoyVehicleReadModel vehicle, IReadOnlyList<Requirement> requirements)
    {
        var reasons = requirements.Where(IsOutstandingBlocker).Select(requirement => requirement.Message).ToList();
        var advisories = requirements
            .Where(requirement => requirement.Severity == RequirementSeverity.Advisory)
            .Select(requirement => requirement.Message)
            .ToList();
        var insured = requirements.Single(requirement => requirement.Code == "insurance").State == RequirementState.Done;

        return new VehicleReadinessReadModel(vehicle.Vin, vehicle.Plate, insured, reasons.Count == 0, reasons, advisories);
    }

    private static List<Requirement> VehicleRequirements(
        int convoyId, VehicleReadinessFacts entry, ConvoyReadModel convoy, DateTime asOf)
    {
        var vehicle = entry.Vehicle;
        var vin = vehicle.Vin;
        var at = $"/convoys/{convoyId}/vehicles/{vin}";

        var insuranceProblem = entry.Policy switch
        {
            null => "Insurance not recorded",
            { Voided: true } => "Insurance voided",
            { } policy when !policy.CoversOn(asOf) => "Insurance does not cover the departure date",
            { CoversAllDrivers: false } => "Insurance does not cover every driver",
            _ => null,
        };

        var requirements = new List<Requirement>
        {
            Vehicle("driver", vin, vehicle.DriverCount >= DriversRequired, "Dispatcher", "No driver assigned", $"{at}/crew"),
            Vehicle("insurance", vin, insuranceProblem is null, "Dispatcher", insuranceProblem ?? "Insurance in cover", $"{at}/insurance"),
            Vehicle("ferry", vin, entry.Ferry is not null, "Dispatcher", "No ferry booking", $"{at}/ferry"),
            Vehicle(
                "handover-receiver",
                vin,
                entry.HandoverReceiverRegistered,
                "Dispatcher",
                "No registered handover Receiver",
                $"{at}/handover-receiver"),
            Vehicle(
                "boxes",
                vin,
                entry.Boxes.All(box => box.Validated && box.ReceiverRegistered),
                "Loader",
                "A box is not validated or has no registered Receiver",
                $"{at}/boxes"),
        };

        requirements.AddRange(DeclarationRequirements(entry, at));
        requirements.AddRange(Advisories(entry, convoy, at));

        return requirements;
    }

    private static IEnumerable<Requirement> DeclarationRequirements(VehicleReadinessFacts entry, string at)
    {
        var vin = entry.Vehicle.Vin;

        bool Current(DeclarationKind kind, Guid? receiver = null) =>
            entry.Declarations.Any(declaration =>
                declaration.Kind == kind
                && declaration.ReceiverRef == receiver
                && declaration.Status is DeclarationStatus.Filed or DeclarationStatus.Accepted or DeclarationStatus.Closed);

        var ensCurrent = Current(DeclarationKind.Ens);
        var receivers = entry.Boxes.Where(box => box.ReceiverRef is not null).Select(box => box.ReceiverRef).Distinct().ToList();
        var link = $"{at}/declarations";

        yield return Declaration("declaration.gmr", vin, Current(DeclarationKind.Gmr), "Dispatcher", "GMR not declared or not current", link);
        yield return Declaration("declaration.ens", vin, ensCurrent, "Dispatcher", "ENS not declared or not current", link);

        var elo = Current(DeclarationKind.Elo);
        yield return elo
            ? Declaration("declaration.elo", vin, true, "Dispatcher", "ELO declared", link)
            : Vehicle("declaration.elo", vin, false, "Dispatcher", "ELO not declared or not current", link) with
            {
                State = ensCurrent ? RequirementState.ToDo : RequirementState.Blocked,
            };

        yield return Declaration(
            "declaration.goods-list",
            vin,
            receivers.All(receiver => Current(DeclarationKind.GoodsList, receiver)),
            "Dispatcher",
            "A goods list is missing or not current",
            link);
    }

    private static IEnumerable<Requirement> Advisories(VehicleReadinessFacts entry, ConvoyReadModel convoy, string at)
    {
        var vin = entry.Vehicle.Vin;

        if (entry.Vehicle.DriverCount is >= DriversRequired and < DriversAdvised)
        {
            yield return Advisory("advisory.second-driver", vin, "Dispatcher", "Only one driver; two are advised", $"{at}/crew");
        }

        if (entry.Policy is { Voided: false } policy && policy.CoverEnd < convoy.ExpectedEnd)
        {
            yield return Advisory(
                "advisory.insurance-journey", vin, "Dispatcher", "Insurance ends before the journey does", $"{at}/insurance");
        }

        if (entry.Ferry is { TicketDetails: null or "" })
        {
            yield return Advisory("advisory.ferry-ticket", vin, "Dispatcher", "Ferry ticket details are missing", $"{at}/ferry");
        }

        if (entry.Items.WithoutValue > 0 || entry.Items.WithoutDonor > 0)
        {
            yield return Advisory(
                "advisory.item-incomplete", vin, "Loader", "An item has no value or no donor", $"{at}/boxes");
        }

        if (entry.Items.Expired > 0 || entry.Items.ShortShelfLife > 0)
        {
            yield return Advisory(
                "advisory.shelf-life", vin, "Loader", "An item is expired or short of the required shelf life", $"{at}/boxes");
        }
    }

    private static Requirement Blocking(string code, bool done, string owner, string message, string link) =>
        new(code, RequirementScope.Convoy, null, done ? RequirementState.Done : RequirementState.ToDo, owner, RequirementSeverity.Blocking, message, link);

    private static Requirement Vehicle(string code, string vin, bool done, string owner, string message, string link) =>
        new(code, RequirementScope.Vehicle, vin, done ? RequirementState.Done : RequirementState.ToDo, owner, RequirementSeverity.Blocking, message, link);

    private static Requirement Declaration(string code, string vin, bool done, string owner, string message, string link) =>
        Vehicle(code, vin, done, owner, message, link);

    private static Requirement Advisory(string code, string? vin, string owner, string message, string link) =>
        new(code, vin is null ? RequirementScope.Convoy : RequirementScope.Vehicle, vin, RequirementState.Warning, owner, RequirementSeverity.Advisory, message, link);
}

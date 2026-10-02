# 8. Readiness is computed from the facts, and blocking requirements are never overridden

Date: 2026-10-02

## Status

Accepted. Not yet implemented. Amends [ADR 0001](0001-truck-list-as-a-table.md).

## Context

Readiness today is a single pure function, `ConvoyReadiness.Assess` (`Application/Convoys/ReadinessUseCases.cs`).
It checks that a convoy has a route, that it has vehicles still travelling, and that each vehicle has **two drivers
on each leg** and recorded, unvoided, in-cover insurance. It is **advisory**: it reports what is missing and blocks
nothing. The only things that actually stop a vehicle departing are checks inside the manifest's `depart`
transition, and the one that is checked there is insurance.

A Dispatcher needs the opposite. They need to see, in one place, **everything that must be true before a convoy
leaves, and what is still outstanding**, covering facts that did not exist when readiness was written: ferry
bookings, accommodation, a Convoy Leader, registered Receivers, validated boxes and current declarations. Some of
those must stop a convoy, and the project owner was explicit that **they are not to be overridden**: "they are
business rules (we should not be driving without insurance)"
([decision O3](../domain/decisions.md#o3)).

## Decision

### Readiness is a set of requirements computed from the facts, and is never stored

Each requirement has a **scope** (the convoy or one vehicle), a **state** (done, to do, blocked, warning), an
**owner role**, a **severity** (*blocking* or *advisory*), and a **link to where it is resolved**. Nothing can fall
out of step with the facts, and a new rule is one more requirement, not a new status. A withdrawn vehicle is
skipped, as today.

### The blocking requirements

These stop a convoy departing ([decision P4](../domain/decisions.md#p4)). The full table is in
[Convoy operations](../domain/convoy-operations.md#blocking-requirements).

| Scope | Requirement |
| --- | --- |
| Convoy | A Convoy Leader is assigned |
| Convoy | Every crew member has accommodation at every overnight stop, **or is flagged as arranging their own** ([O4](../domain/decisions.md#o4)) |
| Vehicle | At least **one** driver ([P9](../domain/decisions.md#p9)) |
| Vehicle | Insurance recorded, in cover, and covering **every driver** ([O7](../domain/decisions.md#o7)) |
| Vehicle | A ferry booking exists |
| Vehicle | A handover Receiver is set and **registered** ([P11](../domain/decisions.md#p11)) |
| Vehicle | Every allocated box is validated and has a **registered** Receiver ([D35](../domain/decisions.md#d35)) |
| Vehicle | Declarations are **current** ([ADR 0005](0005-declarations-are-per-vehicle-with-derived-staleness.md)) |

Two existing rules carry over: the convoy has a route, and a vehicle must have passed inspection to join a truck
list. Everything else is **advisory** ([decision P17](../domain/decisions.md#p17)), including a second driver.

### Blocking requirements are not overridden

There is no override, no waiver and no "depart anyway" role. Where a real need exists, **the rule is changed**, and
the discussion that produced this ADR did so once already: an *unbooked hotel* looked like a case for an override,
and the answer was a **self-accommodation flag** instead ([decision O4](../domain/decisions.md#o4)). A rule that is
wrong is fixed in the rule, in the open, with a decision record.

### Insurance covers named drivers, and only adding a driver needs it updated

The earlier rule, that **any crew change voids the insurance**, is replaced. Removing a crew member leaves the
remaining drivers covered. **Adding a driver** needs the policy updated with the insurer, which the Dispatcher
records. There is no cut-off after which a change needs extra approval
([decision O7](../domain/decisions.md#o7)). The blocking requirement is therefore *every driver on the vehicle is
covered*, not *the insurance is unvoided*.

## Alternatives considered

**A stored `Ready` status** set by a button. Cheap to read, and wrong the moment a fact changes behind it.

**Keep readiness advisory** and rely on the Dispatcher. This is the current state, and the reason a convoy can
depart missing things nobody noticed.

**Blocking with an Administrator override and a recorded reason.** The conventional answer, and explicitly
declined. An override makes every blocker negotiable, which is how a vehicle ends up on the road uninsured.

## Consequences

**Departure must refuse with the reasons.** The `depart` transition and any convoy-level departure check the
blocking requirements for the convoy and for that vehicle, and a refusal names each unmet requirement and where to
fix it, as the manifest's insurance refusal does today.

**Insurance needs a coverage model.** `VehicleInsurance.InCover` is currently the one statement of the rule and the
crew-change transactions (`AssignCrewAsync`, `UnassignCrewAsync`) void it. Both change: removal no longer voids it,
and the policy must record **which drivers it covers**, so that an added driver is visibly uncovered until the
Dispatcher updates it.

**Accommodation needs an owner per person per stop.** A booking covers one or more crew members at one route point,
and a self-accommodation flag per person per stop satisfies the requirement
([Q-self-accommodation](../domain/decisions.md#q-self-accommodation)).

**Each requirement is a small, testable unit.** `ConvoyReadiness.Assess` stays a pure function, and grows a
requirement list in place of three hard-coded checks. At roughly ten vehicles a convoy and about 25 users
([O23](../domain/decisions.md#o23)), computing it on read is not a cost worth caching.

**Departure surfaces three things that were invisible.** A stale declaration, an uncovered added driver and an
unregistered Receiver all become blocking, so a Dispatcher will meet them in the days before departure, which is
the point.

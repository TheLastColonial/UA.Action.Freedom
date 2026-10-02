# 7. Journey legs are removed from the crew model

Date: 2026-10-02

## Status

Accepted. Not yet implemented. Supersedes part of [ADR 0001](0001-truck-list-as-a-table.md).

## Context

[ADR 0001](0001-truck-list-as-a-table.md) renamed `dbo.VehicleDriver` to `dbo.ConvoyVehicleCrew` and gave it a
**`Leg`**: `Uk` (UK to Europe) and `Border` (Europe to Ukraine). One seat per person became **per leg**
(`UQ_ConvoyVehicleCrew_Convoy_Person_Leg`), "because a crew handover at the European border is a real event and is
the reason the leg exists". Readiness asked for two drivers *on each leg*, and `JourneyLeg` appears throughout the
domain, the database, the API (a required `leg` in the crew body), the validators and every test project.

Product discovery found that the leg is not wanted for now. The project owner decided to **remove the concept
from the model and reintroduce it later if it is needed** ([decision P12](../domain/decisions.md#p12)). Three
things were learned while doing so:

- The leg modelled a **handover of crew**, but what the business needs at a border is an **event**, a crossing,
  marked by the Convoy Leader. That is a fact about a route point
  ([ADR 0005](0005-declarations-are-per-vehicle-with-derived-staleness.md)), not a property of a crew seat.
- "Two drivers on each leg" was an advisory rule. The rule that matters is **at least one driver per vehicle**,
  and that blocks departure ([decision P9](../domain/decisions.md#p9)).
- Accommodation attaches to a **route point**, not to a leg ([decision P2](../domain/decisions.md#p2)).

## Decision

A **crew seat is one person on one vehicle on one convoy**, with a role of `Driver` or `Passenger`. A person
cannot sit in two vehicles on the same convoy. The journey leg is removed from the domain, the database, the API
and the documentation.

Readiness counts drivers **per vehicle**: one is required to depart and two are advised
([ADR 0008](0008-readiness-is-computed-and-blocking-rules-are-not-overridden.md)).

## Alternatives considered

**Keep the legs and hide them.** Rejected. The leg would still sit in every schema, request body and test, costing
effort to maintain for a capability nobody is using.

**Replace legs with route-point crossings now.** Partly accepted: the crossing is an event the Convoy Leader marks
([decision X2](../domain/decisions.md#x2)), but it records *that a border was crossed*, not *who crewed each side*.
It is not a substitute for crew-per-leg, and this ADR does not claim it is.

## Consequences

**A capability is lost.** A crew handover at the European border can no longer be recorded. If it is needed again,
legs are reintroduced as a deliberate decision, and the crossing events from ADR 0005 are the natural starting
point.

**The change is wide and breaking.** It touches `ConvoyVehicleCrew` and its unique key, `AssignCrewAsync` and
`UnassignCrewAsync`, `ConvoyReadiness.Assess`, the `PUT /convoys/{id}/vehicles/{vin}/crew/{personId}` body, its
validators, and the unit, component, integration and BDD tests. The schema is rebuilt from scratch as for every
schema change in this project, so there is no migration, but the removal should be its own slice, and first,
because everything else touches crew.

**The `Unique` constraint simplifies.** It becomes one seat per person per convoy, which the database still
enforces.

**Documents behind:** [gotchas-and-open-questions.md](../gotchas-and-open-questions.md) (the crew-per-leg notes and
the commitment note), [local-authentication.md](../local-authentication.md) ("per journey leg") and `CLAUDE.md`
all describe legs and are listed in [decisions](../domain/decisions.md#consequences-and-amendments-due).

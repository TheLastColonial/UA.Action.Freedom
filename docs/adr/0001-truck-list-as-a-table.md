# 1. The truck list is a table, and the manifest is its child

Date: 2026-09-20

## Status

Accepted. **Partly superseded on 2026-10-02.** Crew are no longer keyed by leg
([ADR 0007](0007-journey-legs-are-removed-from-the-crew-model.md)), and the manifest is no longer the
central document or the home of a vehicle's cargo
([ADR 0004](0004-the-manifest-is-the-load-sign-off.md)). The truck list as a table, withdrawal as a stamp,
and one manifest per entry all still stand. See [Amendments](#amendments).

## Context

`Convoy` and `Manifest` were suspected of duplicating each other. They do not: the split is stated
doctrine in three places — `docs/domain/key-concepts.md` § Convoy, the header comment on
`dbo.Convoy`, and `CLAUDE.md` — and it reads the same in all of them.

> The convoy is the unit that is planned; the manifest is the unit that is executed per vehicle.

What *was* duplicated is the fact underneath both. "This vehicle is travelling with this convoy"
was written in four places and reconciled in none:

| Where | Shape |
| --- | --- |
| `dbo.Vehicle.ConvoyId` | a single mutable pointer, `ON DELETE SET NULL` |
| `dbo.Manifest.ConvoyId` + `Vin` | two independent nullable foreign keys |
| `dbo.VehicleDriver` | keyed `(ConvoyId, Vin, PersonId)`, keeping history |
| `dbo.VehicleInsurance` | keyed `(ConvoyId, Vin)`, keeping history |

Three consequences, all of which looked like features until they were written down:

1. **An arrived convoy lost its own truck list.** `POST /convoys/{id}/arrive` nulled
   `Vehicle.ConvoyId` to *release* a Returned vehicle. "Which vehicles were on convoy 5?" then had
   no answer, while the crew and insurance rows — which *do* keep history — went on naming a parent
   that no longer existed.
2. **Nothing tied a manifest to a real truck-list entry.** `CreateManifestHandler` accepted any
   `(Vin, ConvoyId)` pair, and no constraint stopped one vehicle carrying two manifests. That is
   load-bearing: `ArriveAsync` asks each vehicle for its finished manifest, and two would have been
   satisfied by whichever finished first.
3. **Two crew records, unconnected.** `dbo.VehicleDriver` decided the insurance — which a crew
   change voids, and which gates departure — while `dbo.ManifestDriverTeam` decided nothing at all.
   A printed manifest could name people who were not in the vehicle.

Separately, the charity confirmed a requirement none of this could express: a vehicle that **breaks
down mid-journey** leaves the convoy, and may later be repaired and join another, or make its own
way. Its manifest and GMR still describe a real load.

## Decision

**Make the truck list a table: `dbo.ConvoyVehicle`, keyed `(ConvoyId, Vin)`.** Hang the crew, the
insurance and the manifest off it.

- `dbo.Vehicle.ConvoyId` is **removed**. `VehicleReadModel.ConvoyId` is derived from the truck list
  on the way out, so it cannot drift from it.
- `dbo.Manifest.(ConvoyId, Vin)` becomes `NOT NULL`, a composite foreign key to the entry, and
  unique. A manifest is opened at `POST /convoys/{id}/vehicles/{vin}/manifest`; there is no
  `POST /manifests`, and `PUT /manifests/{id}` has no field for either column.
- **Withdrawal is a stamp, not a delete.** `WithdrawnAt` + `WithdrawnReason` record a vehicle
  leaving a published convoy, keeping the entry, its crew, its insurance and its manifest.
- `dbo.ManifestDriverTeam` is **dropped**. `dbo.VehicleDriver` becomes `dbo.ConvoyVehicleCrew` and
  gains a `Leg`; one seat per person is now per leg, not per convoy, which is what makes a crew
  handover at the European border recordable. `GET /manifests/{id}/crew` reads it.
- The application port splits along the same boundary: `IConvoyRepository` (the journey) and
  `IConvoyVehicleRepository` (the truck list, crew and insurance), replacing one 21-method
  interface.

Only one cascade path may reach a child, so `ConvoyVehicle → Vehicle` cascades and
`ConvoyVehicle → Convoy` does not; `ConvoyRepository.DeleteAsync` clears the truck list itself in
the transaction that deletes the convoy.

## Consequences

**Better.** "Which vehicles were on convoy 5?" is answerable after arrival. A manifest cannot name
a truck that is not on its convoy, and one vehicle on one convoy carries exactly one manifest —
both enforced by the database rather than by a handler. A crew change voids the insurance for the
crew that actually travels. A breakdown is expressible, and loses nothing.

**Worse, or at least more.** Two repositories and two ports where there was one of each. Crewing
takes a leg, so every caller — API, UI, tests, BDD — names one. The convoy and the vehicle are no
longer editable on a manifest, which is a deliberate loss of flexibility.

**Given up.** The primary/secondary driver distinction. Nothing used it, and readiness counts
drivers; if the printed manifest later needs a named lead, add `IsLead` to the crew row rather than
resurrecting the team table.

**Left open.** Whether a withdrawn vehicle's manifest should be re-issued when it joins a later
convoy — see `docs/gotchas-and-open-questions.md` §9.14. The implemented reading is that a manifest
is bound to one crossing, because its GMR named that convoy's departure.

**Migration.** None. The schema is end-state-only and every stack is rebuilt from scratch; the
local database was dropped and re-published rather than altered.

## Amendments

Added 2026-10-02. The original text above is kept as it was decided.

| Part of this ADR | Now |
| --- | --- |
| `dbo.ConvoyVehicleCrew` "gains a `Leg`", and one seat per person is **per leg** | **Superseded** by [ADR 0007](0007-journey-legs-are-removed-from-the-crew-model.md). A seat is one person on one vehicle on one convoy. |
| "A crew change **voids** the insurance" | **Superseded** by [ADR 0008](0008-readiness-is-computed-and-blocking-rules-are-not-overridden.md). Only adding a driver needs the insurance updated. |
| The manifest is "the document *pack* for one `ConvoyVehicle`" carrying the boxes, the GMR, the ferry booking and delivery notes | **Superseded** by [ADR 0004](0004-the-manifest-is-the-load-sign-off.md). It is the load sign-off. Boxes, the ferry booking and delivery attach to the truck-list entry, and customs paperwork becomes [declarations](0005-declarations-are-per-vehicle-with-derived-staleness.md). |
| "Given up. The primary/secondary driver distinction." | **Stands.** The Convoy Leader is one per convoy ([decision P7](../domain/decisions.md#p7)), not a lead driver per vehicle. |
| "Which vehicles were on convoy 5?" is answerable after arrival | **Stands.** Closing a convoy ([ADR 0015](0015-box-and-vehicle-outcomes-and-convoy-closing.md)) adds a report and keeps the list. |

# 14. Box and vehicle outcomes, acceptance, and closing a convoy

| | |
|---|---|
| **Branch** | `feat/outcomes-closing` |
| **Covers** | [ADR 0015](../adr/0015-box-and-vehicle-outcomes-and-convoy-closing.md); [O2](../domain/decisions.md#o2), [O5](../domain/decisions.md#o5), [O8](../domain/decisions.md#o8), [O9](../domain/decisions.md#o9), [O10](../domain/decisions.md#o10), [O24](../domain/decisions.md#o24), [O25](../domain/decisions.md#o25), [O28](../domain/decisions.md#o28), [D29](../domain/decisions.md#d29) |
| **Depends on** | [08](08-declarations-filing.md), [12](12-budget-equipment.md), [13](13-readiness-departure.md) |
| **Gate** | None |
| **Flows** | [08 On the road](../sequences/08-on-the-road.puml) ([process](../process/08-on-the-road.puml)), [09 Delivery, acceptance and closing](../sequences/09-delivery-acceptance-closing.puml) ([process](../process/09-delivery-acceptance-closing.puml)) |

## Context

How a load ends is one question asked of the manifest today: `Delivered`, `Lost` or `Returned`.
`ConvoyRepository.ArriveAsync` (l.232):
1. stamps the convoy first;
2. counts vehicles with no finished manifest;
3. sets `Vehicle.HandedOverAt` for `Delivered` and `Lost`.

That ordering avoids a deadlock found earlier, so preserve it. **Volunteer erasure** (`PersonRepository.cs` l.24 and
l.137) reads the same finished statuses.

ADR 0015 gives **boxes** their own outcomes, records **acceptance per Ukrainian goods list**, and adds **closing** with
a report. This plan builds them, with the Convoy Leader's actions **performed by the Dispatcher** until
[plan 17](17-scoped-permissions.md) gives the leader their own scoped permissions. The manifest's delivery states are
retired in [plan 15](15-manifest-signoff-lifecycle.md), not here.

### Decisions this plan relies on

| Decision | Effect here |
|---|---|
| [O8](../domain/decisions.md#o8) | Delivered when the Convoy Leader or Dispatcher marks it arrived. |
| [O9](../domain/decisions.md#o9), [O24](../domain/decisions.md#o24) | **Accepted per goods list**, by the leader or Dispatcher. Every box on the list becomes accepted. |
| [O2](../domain/decisions.md#o2), [O28](../domain/decisions.md#o28) | A refused box is **seized** (terminal) or **returned to a registered hub the actor chooses**, back to *Arrived* there, label and contents unchanged, reusable. |
| [O5](../domain/decisions.md#o5) | **Undeliverable** with a reason (damaged, stolen, other). Terminal. "Lost" is not a separate outcome. |
| [O10](../domain/decisions.md#o10), [O25](../domain/decisions.md#o25) | **The Dispatcher closes. No lock.** The report is regenerable: expected against actual costs, and boxes delivered or not. **No address.** |

## Increments

### Increment 1: box outcomes in the domain

- **Domain:** a `BoxOutcome { Delivered, Accepted, Seized, ReturnedToHub, Undeliverable }` with legal transitions held
  as data:
  - `Delivered → Accepted`;
  - after departure, any of `Delivered`, `Seized`, `ReturnedToHub` or `Undeliverable`.
- Seized and undeliverable are terminal. Returned-to-hub clears the allocation and sets the box's location to the
  chosen hub.
- **Tests (RED first):** a unit test per edge and per refusal. Returned-to-hub requires a **registered hub**.

### Increment 2: schema and API for box outcomes

- **Schema:** `dbo.BoxOutcome (BoxId, ConvoyId, Outcome, Reason NULL, HubLocationId NULL, RecordedBy, RecordedAt)`,
  one current row per box per convoy, with history kept.
- **Api:** `POST /convoys/{id}/boxes/{boxId}/outcome`, under `convoys:write` (the Dispatcher acting for the leader).
- A refused or undeliverable box **leaves the vehicle's load**, so the vehicle's open declarations go stale through
  [plan 09](09-declaration-staleness.md)'s snapshot comparison. Add a component test that proves it.
- **Tests:** component and integration tests. Add fakes.

### Increment 3: returned to a hub is reusable

- After `ReturnedToHub`, the box is at the hub, unallocated, with the same QR label, and can be allocated to a later
  convoy.
- **Tests:** a component test of allocating it again on a second convoy.

### Increment 4: acceptance per goods list

- `POST /convoys/{id}/vehicles/{vin}/declarations/goods-list/{receiverRef}/accepted` sets every `Delivered` box for
  that Receiver on that vehicle to `Accepted`, in one transaction.
- **Tests:** unit and component tests. Boxes for another Receiver are untouched.

### Increment 5: vehicle delivery and arrival

- `POST /convoys/{id}/vehicles/{vin}/delivered` marks the vehicle delivered.
- **Transitional:** while manifests still hold delivery states, this also moves the vehicle's manifest to `Delivered`,
  so the existing `ArriveAsync`, `HandedOverAt` and erasure guard keep working unchanged. [Plan 15](15-manifest-signoff-lifecycle.md)
  switches them to outcomes.
- **Tests:** component tests, and the existing arrival tests still pass.

### Increment 6: closing and the report

- `POST /convoys/{id}/close`, **Dispatcher**, allowed only after arrival. It stamps `ClosedAt` (or re-stamps, since
  there is **no lock**) and returns the report.
- `GET /convoys/{id}/report` regenerates it at any time. It contains:
  - budget lines against actuals ([plan 12](12-budget-equipment.md));
  - per vehicle, boxes by outcome, counts and value (excluding seized and undeliverable from **delivered** value);
  - vehicle values ([O11](../domain/decisions.md#o11));
  - **no address, contact or delivery detail**: Receivers at organisation and region only, or aggregated.
- **Tests:** a unit test for the report calculation. A component test that **asserts the absence** of address fields,
  and that corrections after closing change the regenerated report.

### Increment 7: web

- An outcome control per box on the convoy's vehicle panel (Dispatcher), a hub picker for returned boxes, acceptance
  per goods list on the declarations panel, a close button, and a report page with print styles.
- Tests: Vitest with MSW; a Playwright smoke test of closing a convoy.

### Increment 8: BDD

- `Features/Outcomes.feature`:
  - deliver and accept a goods list;
  - seize one box, which makes the declarations stale;
  - return a box to a hub and re-allocate it;
  - close the convoy;
  - read the report, which has no address.

## Retires and transitional

- **Transitional:** manifests still hold `Delivered/Lost/Returned`, mirrored from vehicle delivery, until
  [plan 15](15-manifest-signoff-lifecycle.md). Leader actions are performed by the Dispatcher until
  [plan 18](18-leader-checklist-progress.md).

## Docs to update

- `CLAUDE.md`: outcomes, acceptance, closing, and the API list.
- `README.md`: endpoints.
- `docs/domain/key-concepts.md` § Arrival and § Box. Remove those rows from the decisions amendments table.
- ADR 0015: an implementation note.

## Risks

- **Two readers of "finished".** Do not change `ArriveAsync` or the erasure guard here. Mirror to the manifest and
  leave the switch to plan 15, which writes the erasure tests first.
- **The value definitions must agree** between the closing report and the value report. Use one function for "value
  delivered".

## Testing

Outcome rules (Unit), persistence (Integration), every route and the report's absence test (Component), the web
controls, and BDD.

## Verification

The standard gates, plus on the local stack: depart a seeded convoy, mark one box seized and one returned to a hub, and
deliver the rest. Accept one Receiver's goods list. Arrive, then close. The report shows the expected against actual
costs, delivered value without the seized box, and no address anywhere.

## Sequencing

After plans [08](08-declarations-filing.md), [12](12-budget-equipment.md) and [13](13-readiness-departure.md). Needed
by [plan 15](15-manifest-signoff-lifecycle.md) and [plan 18](18-leader-checklist-progress.md).

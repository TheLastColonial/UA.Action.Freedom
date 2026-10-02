# 13. Readiness as requirements, and one departure action

| | |
|---|---|
| **Branch** | `feat/readiness-departure` |
| **Covers** | [ADR 0008](../adr/0008-readiness-is-computed-and-blocking-rules-are-not-overridden.md); [P4](../domain/decisions.md#p4), [P9](../domain/decisions.md#p9), [P11](../domain/decisions.md#p11), [P17](../domain/decisions.md#p17), [O3](../domain/decisions.md#o3), [O7](../domain/decisions.md#o7), [O36](../domain/decisions.md#o36), [D10](../domain/decisions.md#d10), [D7](../domain/decisions.md#d7), [D34](../domain/decisions.md#d34) |
| **Depends on** | [01](01-crew-without-legs.md), [04](04-receiver-registration.md), [07](07-box-allocation-ferry.md), [09](09-declaration-staleness.md), [11](11-accommodation.md), [12](12-budget-equipment.md) |
| **Gate** | None |

## Context

`ConvoyReadiness.Assess` (`src/UA.Action.Freedom.Application/Convoys/ReadinessUseCases.cs`) is a pure function. It
returns `Ready` and a list of English reason strings, and **it blocks nothing**. The only departure check is in
`TransitionManifestHandler.Decide` (`ManifestTransitionUseCases.cs` l.80–113): moving a manifest to `InTransit`
requires `IsInsuredToday`, using `DateTime.UtcNow`, while readiness uses `convoy.Start`. **The two can disagree.**

Every fact the blocking requirements need now exists:
- crew and named-driver insurance ([plan 01](01-crew-without-legs.md));
- registered Receivers and handover Receivers ([plan 04](04-receiver-registration.md));
- allocations and ferry bookings ([plan 07](07-box-allocation-ferry.md));
- declarations and staleness ([plan 09](09-declaration-staleness.md));
- the leader and accommodation ([plans 10](10-route-points-convoy-leader.md), [11](11-accommodation.md));
- the budget ([plan 12](12-budget-equipment.md)).

### Decisions this plan relies on

| Decision | Effect here |
|---|---|
| [ADR 0008](../adr/0008-readiness-is-computed-and-blocking-rules-are-not-overridden.md) | Requirements carry a scope, state, owner role, severity and link. Computed, never stored. |
| [P4](../domain/decisions.md#p4) and [the blocking table](../domain/convoy-operations.md#blocking-requirements) | The blocking set. |
| [O3](../domain/decisions.md#o3) | **No override, no waiver, no "depart anyway".** |
| [O36](../domain/decisions.md#o36) | **One `depart` action on the convoy, by the Dispatcher.** It replaces the manifest's per-vehicle `depart`. |
| [P17](../domain/decisions.md#p17) and [the advisory table](../domain/convoy-operations.md#advisory-warnings) | The warnings. |

## Increments

### Increment 1: the requirement shape and one clock

- **Domain:** `Requirement(Code, Scope { Convoy | Vehicle(vin) }, State { Done, ToDo, Blocked, Warning }, OwnerRole,
  Severity { Blocking, Advisory }, ResolveLink)`.
- `ConvoyReadiness.Assess(facts, asOf)` takes an **injected clock**: one `asOf` for every date rule, so the
  insurance-on-day check and readiness agree.
- **Tests (RED first):** unit tests that the shape is returned, and that a withdrawn vehicle is skipped.

### Increment 2: blocking requirements, one test each

One unit test per row of [the blocking table](../domain/convoy-operations.md#blocking-requirements), each with a
passing and a failing case:
- Convoy Leader assigned;
- accommodation covered or self-arranged at every overnight stop;
- at least one driver;
- insurance in cover on `asOf` and covering every driver;
- ferry booking exists;
- handover Receiver set and registered;
- every allocated box validated, with a registered Receiver;
- declarations current for every instrument (none stale, refused or missing, and no open re-declare task).

The route and inspection rules carry over.

### Increment 3: advisory warnings

One unit test per row of [the advisory table](../domain/convoy-operations.md#advisory-warnings): two drivers; sensitive
cargo unacknowledged (needs an "acknowledge" action, small); expired or short shelf life; a deadline within a week;
over capacity; a leftover booking; insurance not covering the whole journey; a booking's dates against the route's
times; ferry timing; an item missing category, value or donor; a Receiver registration expiring; budget.

- Where a warning needs data that does not exist yet (for example a registration expiry date), add the minimal field
  and record it in the PR, or leave the warning out and **list it as deferred**. Do not invent rules.

### Increment 4: the facts loader

- `GetConvoyReadinessHandler` loads all the facts in as few queries as practical: crew, insurance, ferry, allocations
  with box validation and Receiver status, declarations with staleness, accommodation coverage, the leader, budget and
  items.
- **Tests:** a component test of the full JSON contract, and integration tests for any new queries.

### Increment 5: `POST /convoys/{id}/depart`

- Under `convoys:write`, **the Dispatcher** (and the Administrator, if the policy includes them).
- It returns **409** with the list of unmet blocking requirements and their links, or **200**, stamping a new
  `dbo.Convoy.DepartedAt` as a conditional `UPDATE … WHERE DepartedAt IS NULL`.
- **Transitional:** departure also moves each travelling vehicle's manifest to `InTransit`, so the legacy arrival flow
  keeps working until [plan 15](15-manifest-signoff-lifecycle.md).
- **Remove** the manifest `depart` verb (`MapTransition` in `ManifestEndpoints.cs` l.228–236) and the `NotInsured` check
  in `TransitionManifestHandler`.
- **Tests:**
  - Component: refused with reasons; succeeds; a second call is refused.
  - **No role can bypass it**: a test for each role.
  - Integration: the conditional stamp.

### Increment 6: web

- `web/src/pages/convoys/ConvoyReadinessPanel.tsx` groups blocking and advisory items by scope, each linking to where it
  is fixed.
- A **Depart** button that shows the refusal list on 409.
- Remove the depart verb from `web/src/pages/manifests/transitions.ts`.
- Tests: Vitest with MSW; a Playwright smoke test of the depart refusal.

### Increment 7: BDD

- `Features/Departure.feature`:
  - departure is refused and lists each missing item, as setup is completed one step at a time;
  - it succeeds when all are met;
  - there is no override route.

## Retires and transitional

- **Retires:**
  - advisory-only readiness;
  - the per-leg reasons (already gone in plan 01);
  - the manifest `depart` transition and its insurance check;
  - the readiness `convoy.Start` versus `UtcNow` mismatch.
- **Transitional:** departure moves manifests to `InTransit`. The rest of the manifest delivery states are retired in
  [plan 15](15-manifest-signoff-lifecycle.md).

## Docs to update

- `CLAUDE.md`: readiness, departure, the API list, and the manifest transitions list.
- `README.md`: endpoints.
- `docs/domain/key-concepts.md` § Readiness: departure is a convoy action.
- `docs/process.puml`, if it shows a per-manifest departure.
- ADR 0008: an implementation note.
- Remove the row from the decisions amendments table.

## Risks

- **The widest read in the system.** Keep the loader to a bounded number of queries, and test with a realistic seed of
  about ten vehicles.
- **Requirement codes become an API contract.** The web links and tests depend on them, so treat them as stable names.

## Testing

A unit test per requirement and per warning, the loader (Component and Integration), depart (Component, BDD and every
role), and the web panel.

## Verification

The standard gates, plus on the local stack: on the seeded convoy, departure is refused with a list. Fix each item from
its link, and departure succeeds once. A second departure is refused.

## Sequencing

After plans 01, 04, 07, 09, 11 and 12. Needed by [plan 14](14-outcomes-closing.md) and
[plan 15](15-manifest-signoff-lifecycle.md).

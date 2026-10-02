# 10. Stable route points, and nominating the Convoy Leader

| | |
|---|---|
| **Branch** | `feat/route-points-convoy-leader` |
| **Covers** | [P15](../domain/decisions.md#p15), [D8](../domain/decisions.md#d8), [D17](../domain/decisions.md#d17), [P7](../domain/decisions.md#p7), [P14](../domain/decisions.md#p14); the route-point groundwork for [X2](../domain/decisions.md#x2) and [ADR 0016](../adr/0016-progress-is-reported-not-tracked.md) |
| **Depends on** | [01](01-crew-without-legs.md), [03](03-last-changed-audit.md) |
| **Gate** | None |

## Context

A route is `dbo.ConvoyRouteStop`, keyed on `(ConvoyId, Sequence)`, with columns `House`, `Street`, `City`, `Country`,
`CountryCode` and `Postcode`. **It has no kind, flag, name or stable id.** `PUT /convoys/{id}/route` replaces it whole:
`ConvoyRepository.ReplaceRouteAsync` (l.172) **deletes and re-inserts every stop**. The domain `Address` has no
`CountryCode`, although the table does.

Accommodation ([plan 11](11-accommodation.md)), progress marks and crossings ([plan 18](18-leader-checklist-progress.md))
all need to point at a route point **that survives an edit of the route**. This plan makes route points stable and
typed, and adds the Convoy Leader as a nominated, historical assignment.

There is no Convoy Leader today, and `dbo.Convoy` has no leader column.

### Decisions this plan relies on

| Decision | Effect here |
|---|---|
| [P15](../domain/decisions.md#p15) | A point is an **overnight stop only if the Dispatcher flags it**. The service never calculates routes or times. |
| [X2](../domain/decisions.md#x2) | A point can be a **border crossing for a named authority** (UK, EU/France, Ukraine). |
| [D8](../domain/decisions.md#d8), [P7](../domain/decisions.md#p7) | Exactly one Convoy Leader per convoy, a Driver on the convoy. |
| [D17](../domain/decisions.md#d17), [P14](../domain/decisions.md#p14) | The Dispatcher nominates. **Only a Dispatcher or Administrator reassigns.** History is kept. |

## Increments

### Increment 1: route points with stable ids, merged not wiped

- **Domain:** `RoutePoint` (id, sequence, name, kind, address, authority for a border point). Kinds are `Stop`,
  `Overnight`, `Border` and `Hub`.
- **Schema:** `dbo.ConvoyRouteStop` gains `RoutePointId int IDENTITY` as the primary key, with
  `UQ (ConvoyId, Sequence)`, plus `Name`, `Kind int` (with a CHECK) and `Authority int NULL`.
- **Data:** `ReplaceRouteAsync` becomes a **merge**:
  - points matched by id are updated;
  - new points are inserted;
  - removed points are deleted, **unless referenced**, in which case the merge refuses with `PointInUse` (409).
  - One transaction, as today.
- **Tests (RED first):**
  - Integration: ids survive a reorder and an edit; removing a referenced point is refused. For now, reference it from
    a test-only table, or make the check a port that later plans feed.
  - Mirror it in `InMemoryConvoyRepository`.

### Increment 2: route API and kinds

- `PUT /convoys/{id}/route` accepts points **with optional ids**. Points without an id are new.
- The response returns the ids.
- Validators:
  - a `Border` point needs an authority;
  - only `Border` points may have one;
  - names are required.
- The `CountryCode` mismatch: add `CountryCode` to the domain `Address`, so the round trip is lossless.
- **Tests:** component JSON contract tests, and that renumbering 1..n in list order is kept.

### Increment 3: nominating the Convoy Leader

- **Domain:** `ConvoyLeaderAssignment (ConvoyId, PersonId, From, Until)`. One open assignment per convoy. The person
  must be a **Driver crewed on the convoy** ([plan 01](01-crew-without-legs.md)).
- **Schema:** `dbo.ConvoyLeaderAssignment` with a filtered unique index on `(ConvoyId) WHERE Until IS NULL`.
- **Data:** `NominateLeaderAsync` closes the current assignment and opens a new one in **one** transaction.
- **Api:** `PUT /convoys/{id}/leader` `{ personId }` and `GET /convoys/{id}/leader` (current and history). Writes
  require a new `convoys:lead-assign` policy, **Dispatcher and Administrator** ([P14](../domain/decisions.md#p14)).
- **Tests:**
  - Unit: a non-crew person or a passenger is refused.
  - Integration: one open assignment.
  - Component: the policy.
- **Removing the leader from the crew:** decide in the PR whether it closes the leader assignment or is refused while
  they lead. Recommend refusing until a new leader is nominated, so a convoy is never left silently without one.

### Increment 4: web

- `web/src/pages/convoys/RouteEditor.tsx` and `routeModel.ts`: a kind selector, the authority for border points, and
  names. Keep ids through edits.
- A Convoy Leader picker on `ConvoyDetailPage`, with history.
- Update `web/src/auth/policyMatrix.ts`.
- Tests: Vitest with MSW; a Playwright smoke test of route editing that keeps the ids.

### Increment 5: seed and BDD

- Seed: route points with kinds, and a nominated leader.
- **BDD:** `Convoys.feature`:
  - editing a route keeps point ids;
  - the Dispatcher nominates a leader;
  - an operator without the policy cannot reassign.

## Retires and transitional

- **Retires:** delete-and-reinsert of route stops.
- **Transitional:** the "Convoy Leader assigned" blocking requirement arrives in [plan 13](13-readiness-departure.md).
  The leader's own permissions arrive in [plan 17](17-scoped-permissions.md). Until then, a leader is a fact, not a
  login capability.

## Docs to update

- `CLAUDE.md`: the Convoys slice (route merge, the route-point id, leader assignment), the policy list and the API list.
- `README.md`: endpoints and the policy matrix.
- `docs/domain/key-concepts.md` § Route and § Roles: add the Convoy Leader.
- `docs/local-authentication.md`: the policy matrix.
- Gotchas: the route is merged, not replaced.

## Risks

- **The merge semantics are the risk.** A bug here silently re-points accommodation or marks at the wrong stop. Test
  reorder, insert-in-middle, delete, and delete-referenced.
- **Identity key change.** Changing `ConvoyRouteStop`'s primary key requires a rebuild of the local database. That is
  expected: the schema is end-state only.

## Testing

All four .NET layers, the web route editor and leader picker, and BDD.

## Verification

The standard gates, plus on the local stack: create a route with an overnight and a border point, reorder it, and check
the ids are unchanged. Nominate a leader, reassign them, and check the history shows both.

## Sequencing

After plans [01](01-crew-without-legs.md) and [03](03-last-changed-audit.md). Needed by
[plan 11](11-accommodation.md) and [plan 17](17-scoped-permissions.md).

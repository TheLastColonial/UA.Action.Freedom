# 18. The Convoy Leader's checklist, progress marks and crossings

| | |
|---|---|
| **Branch** | `feat/leader-checklist-progress` |
| **Covers** | [ADR 0016](../adr/0016-progress-is-reported-not-tracked.md); [X2](../domain/decisions.md#x2), [X10](../domain/decisions.md#x10), [X13](../domain/decisions.md#x13), [O18](../domain/decisions.md#o18), [O20](../domain/decisions.md#o20), [O27](../domain/decisions.md#o27), the leader's side of [O2](../domain/decisions.md#o2); closing declarations at a crossing ([ADR 0005](../adr/0005-declarations-are-per-vehicle-with-derived-staleness.md)) |
| **Depends on** | [09](09-declaration-staleness.md), [11](11-accommodation.md), [12](12-budget-equipment.md), [14](14-outcomes-closing.md), [17](17-scoped-permissions.md) |
| **Gate** | None |

## Context

Progress is reported, never tracked: there is **no GPS** ([O20](../domain/decisions.md#o20)). The Convoy Leader marks
each route point reached, each accommodation reached, and each border crossed, from a **checklist page on a phone**
([X2](../domain/decisions.md#x2), [X10](../domain/decisions.md#x10)). A Dispatcher may record any mark **on their
behalf**, and is named. Only the time of entry is kept ([O27](../domain/decisions.md#o27)). A crossing **closes** the
declarations for that border, and a closed declaration can no longer go stale.

The following already exist:
- stable route points with kinds and authorities ([plan 10](10-route-points-convoy-leader.md));
- accommodation ([plan 11](11-accommodation.md));
- fuel as a cost type ([plan 12](12-budget-equipment.md));
- outcomes ([plan 14](14-outcomes-closing.md));
- the leader's scoped permission ([plan 17](17-scoped-permissions.md)).

**No addresses on this page yet.** Before the address window it shows headers only ([X13](../domain/decisions.md#x13)),
and the address reads themselves are [plan 19](19-leader-address-access.md).

### Decisions this plan relies on

| Decision | Effect here |
|---|---|
| [O20](../domain/decisions.md#o20) | Marks at route points and accommodation. No location data, ever. |
| [X2](../domain/decisions.md#x2) | A border point's mark is a **crossing** for its authority. |
| [O27](../domain/decisions.md#o27) | A Dispatcher may mark on the leader's behalf, and the record names who. **Entered time only.** |
| ADR 0005 | A crossing **closes** the vehicle's declarations for that authority. Closed ones are frozen and are not compared for staleness. |
| [X10](../domain/decisions.md#x10), [O18](../domain/decisions.md#o18) | A web page, nothing stored on the device. The fallback is to call HQ and use printed documents. |

## Increments

### Increment 1: marks in the domain

- **Domain:** `RoutePointMark(ConvoyId, RoutePointId, Kind { Reached, Crossed }, Vin?, RecordedBy, RecordedAt,
  OnBehalfOf?)`.
- A crossing is allowed only on a `Border` point. A mark is per convoy, or per vehicle where vehicles split.
- **Tests (RED first):** unit tests for the rules, and for "on behalf of" naming the Dispatcher.

### Increment 2: schema and API

- **Schema:** `dbo.RoutePointMark`, with audit columns. Marks reference route points, so they feed plan 10's
  "point in use" refusal.
- **Api:** `POST /convoys/{id}/route/{pointId}/reached` and `/crossed`, plus `GET /convoys/{id}/progress`.
- Allowed for the **leader on their own convoy** (plan 17 scope) and for the **Dispatcher**, who is recorded as acting
  on the leader's behalf.
- **Tests:** component tests for both actors and the scope (the leader of another convoy is denied). Integration tests.

### Increment 3: a crossing closes declarations

- Marking a border crossed moves each **accepted** declaration of that authority, for the vehicles crossing, to
  `Closed`, in the same transaction.
- [Plan 09](09-declaration-staleness.md)'s staleness comparison **skips closed declarations**.
- **Tests:**
  - Unit: a closed declaration is never stale.
  - Component: cross, then change the load, and no new stale task appears for that authority, but other authorities'
    declarations still go stale.

### Increment 4: fuel and box outcomes from the leader

- `POST /convoys/{id}/costs` with type `Fuel` is allowed for the leader on their own convoy, recorded under their name
  ([P14](../domain/decisions.md#p14)).
- The leader can record box outcomes (seized, returned to a hub, undeliverable) from the page, using
  [plan 14](14-outcomes-closing.md)'s routes now scoped by plan 17.
- **Tests:** component tests for each, under the leader's login.

### Increment 5: the checklist page

- A mobile page under `web/src/pages/convoys/` showing:
  - the route's points in order, each with its **name and kind only** (the header);
  - a **Reached** or **Crossed** action per point;
  - accommodation per stop;
  - a fuel entry;
  - box outcomes;
  - HQ's phone number and a "print the route sheet" link for the fallback ([O18](../domain/decisions.md#o18)).
- **Nothing stored on the device:**
  - The API sends `Cache-Control: no-store` on every checklist response. Add middleware for those routes, with a
    component test.
  - The page uses no local storage, session storage, IndexedDB or service worker. Add a Vitest test that the page code
    does not touch them, and a lint rule if practical.
- HQ's progress view on the convoy overview shows the marks.
- Tests: Vitest with MSW; a **Playwright test at a mobile viewport** that marks a point reached and a border crossed.

### Increment 6: BDD

- `Features/Progress.feature`:
  - the leader marks a stop reached;
  - the Dispatcher marks a crossing on their behalf and is named;
  - a crossing closes that border's declarations;
  - the leader of another convoy is refused.

## Retires and transitional

- **Retires:** the Dispatcher-only path for fuel and outcomes. The Dispatcher keeps the on-behalf ability.
- **Not here:** addresses on the page ([plan 19](19-leader-address-access.md)).

## Docs to update

- `CLAUDE.md`: progress marks, the checklist page, no-store, and the API list.
- `README.md`: endpoints.
- `docs/domain/key-concepts.md` § Convoy: progress. Remove the row from the decisions amendments table.
- ADR 0016: an implementation note. ADR 0005: closing at a crossing is now implemented.

## Risks

- **A forgotten crossing** keeps declarations open. The Dispatcher on-behalf path is the mitigation, so test it.
- **Browser caching.** `no-store` must be on every response the page fetches, including errors.

## Testing

Mark rules (Unit), persistence and the point-in-use refusal (Integration), both actors and scope (Component), closing
on crossing (Unit and Component), the page (Vitest and Playwright at a mobile viewport), and BDD.

## Verification

The standard gates, plus on the local stack, in a phone-sized browser as `leader`:
1. Mark the first stop reached and a border crossed. HQ's view shows both.
2. As `operator` (Dispatcher), mark the next point on the leader's behalf. It shows the Dispatcher's name.
3. Check the response headers carry `no-store`.
4. Check the browser's storage is empty.

## Sequencing

After plans 09, 11, 12, 14 and 17. Needed by [plan 19](19-leader-address-access.md).

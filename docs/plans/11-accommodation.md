# 11. Accommodation per crew member per stop

| | |
|---|---|
| **Branch** | `feat/accommodation` |
| **Covers** | [P2](../domain/decisions.md#p2), [P8](../domain/decisions.md#p8), [P13](../domain/decisions.md#p13), [P16](../domain/decisions.md#p16), [O4](../domain/decisions.md#o4), [O30](../domain/decisions.md#o30) |
| **Depends on** | [10](10-route-points-convoy-leader.md) |
| **Gate** | None |
| **Flows** | [04 Convoy planning](../sequences/04-convoy-planning.puml) ([process](../process/04-convoy-planning.puml)), [07 Departure](../sequences/07-departure.puml) ([process](../process/07-departure.puml)) |
| **Diagrams** | [Convoy operations (use cases)](../use-cases/convoy-operations.puml) |

## Context

Accommodation is not modelled. The rules are in
[Convoy operations § Accommodation](../domain/convoy-operations.md#accommodation): booked per crew member, linked to a
route point, may be shared, covering every crew member at every **overnight** stop. Drivers never sleep in the
vehicle, but a person may arrange their own accommodation at a stop. If a crew member leaves, their booking stays
until the Dispatcher deals with it. A replacement can take it over.

Stable route-point ids exist from [plan 10](10-route-points-convoy-leader.md).

### Decisions this plan relies on

| Decision | Effect here |
|---|---|
| [P2](../domain/decisions.md#p2), [P8](../domain/decisions.md#p8) | A booking is at a route point and covers one or more crew members. Every crew member at every overnight-flagged stop must be covered. |
| [O4](../domain/decisions.md#o4), [O30](../domain/decisions.md#o30) | A **self-accommodation flag per crew member, per overnight stop** satisfies the requirement for that person. |
| [P13](../domain/decisions.md#p13) | A departed crew member's booking stays. It can be migrated to a replacement. |
| [P16](../domain/decisions.md#p16) | That leftover booking is a **warning and a Dispatcher task** ("cancel or migrate"). It does not block. |

## Increments

### Increment 1: coverage, as a pure function

- **Domain:** `Accommodation.Coverage(overnightPoints, crew, bookings, selfArranged)` returns, per crew member per
  overnight point, `Booked`, `SelfArranged` or `Missing`.
- `LeftoverBookings(bookings, crew)` returns bookings that cover nobody currently crewed.
- **Tests (RED first):**
  - a unit table: all booked; one shared booking covering two people; one person self-arranged; one missing;
  - a non-overnight point needs nothing;
  - a removed crew member's booking becomes leftover.

### Increment 2: schema and repository

- **Schema:**
  - `dbo.AccommodationBooking (Id, ConvoyId, RoutePointId, Provider, Reference, CheckIn, CheckOut, Details,
    CostGbp NULL, Cancelled bit, …)`;
  - `dbo.AccommodationBookingGuest (BookingId, PersonId)`;
  - `dbo.SelfAccommodation (ConvoyId, RoutePointId, PersonId)`;
  - all with the audit columns.
- A booking references a route point, so [plan 10](10-route-points-convoy-leader.md)'s merge refuses to remove that
  point. Wire the "point in use" check to these tables.
- `CostGbp` feeds [plan 12](12-budget-equipment.md).
- **Tests:** integration tests for the round trips and the referenced-point refusal. Add fakes.

### Increment 3: API

- `GET|POST /convoys/{id}/accommodation`, `PUT|DELETE /convoys/{id}/accommodation/{bookingId}`,
  `PUT|DELETE /convoys/{id}/accommodation/self/{routePointId}/{personId}`, under `convoys:write`.
- `GET /convoys/{id}/accommodation/coverage` returns the coverage grid.
- **Migrate:** `POST /convoys/{id}/accommodation/{bookingId}/migrate` `{ fromPersonId, toPersonId }`.
- **Tests:** component tests for the policy and contracts, and migration replacing a guest.

### Increment 4: leftover bookings become tasks

- A leftover booking shows as a **warning** on the coverage response, and as a Dispatcher **task** on the on-screen task
  list from [plan 09](09-declaration-staleness.md), with "cancel or migrate" actions.
- **Tests:** a component test: remove a crewed person with a booking, and a task appears. Cancel the booking, and it
  clears.

### Increment 5: web

- An accommodation panel on the convoy: a grid of overnight stops by crew member, with booked, self-arranged or missing
  in each cell; booking forms; a self-accommodation toggle per cell; and migration from the task.
- Tests: Vitest with MSW; a Playwright smoke test of booking and coverage.

### Increment 6: BDD

- `Features/Accommodation.feature`:
  - full coverage by a shared booking;
  - a self-arranged night;
  - a removed crew member's booking becomes a task and is migrated to the replacement.

## Retires and transitional

- **Transitional:** coverage does not block departure until [plan 13](13-readiness-departure.md) adds it as a blocking
  requirement.

## Docs to update

- `CLAUDE.md`: a new accommodation part of the Convoys slice, and the API list.
- `README.md`: endpoints.
- `docs/domain/key-concepts.md`: add an Accommodation entry.
- Remove the row from the decisions amendments table.

## Risks

- **Per-person bookings at roughly ten vehicles and two crew each** means a grid of around twenty people by the number
  of overnight stops. Keep the coverage endpoint one query, not one call per cell.
- **A booking is personal data about a volunteer.** Erasing a volunteer must not leave their name in booking details.
  Store guests by `PersonId` only.

## Testing

The coverage table (Unit), persistence and the referenced point (Integration), API and tasks (Component), web grid,
and BDD.

## Verification

The standard gates, plus on the local stack: flag two overnight stops, book a shared room for two drivers on night one,
mark one as self-arranged on night two, and leave the other missing. The grid shows exactly one missing cell. Remove a
booked driver, and a "cancel or migrate" task appears.

## Sequencing

After [plan 10](10-route-points-convoy-leader.md). Needed by [plan 13](13-readiness-departure.md) and
[plan 18](18-leader-checklist-progress.md).

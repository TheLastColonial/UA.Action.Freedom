# 09. Declaration staleness and the re-declare task

| | |
|---|---|
| **Branch** | `feat/declaration-staleness` |
| **Covers** | [ADR 0005](../adr/0005-declarations-are-per-vehicle-with-derived-staleness.md) (snapshot and staleness); [D13](../domain/decisions.md#d13), [D24](../domain/decisions.md#d24), [D27](../domain/decisions.md#d27), [D31](../domain/decisions.md#d31), [O21](../domain/decisions.md#o21) |
| **Depends on** | [08](08-declarations-filing.md) |
| **Gate** | None |
| **Flows** | [06 Load change and re-declare](../sequences/06-load-change-and-redeclare.puml) ([process](../process/06-load-change-and-redeclare.puml)) |

## Context

After [plan 08](08-declarations-filing.md) each vehicle has declarations with a lifecycle, but nothing notices when the
load changes after filing. ADR 0005 makes staleness **derived from a snapshot**: when a declaration becomes *ready to
file*, it stores what it was written from, and it is stale whenever the current load differs. No one sets the flag, so
no one can forget to.

Rules: [Customs declarations § Staleness](../domain/customs-declarations.md#staleness).

### Decisions this plan relies on

| Decision | Effect here |
|---|---|
| ADR 0005 | Snapshot at *ready to file*. Stale = current load differs. Never set by hand. |
| [D13](../domain/decisions.md#d13), [D27](../domain/decisions.md#d27) | A move makes the declarations of **both** vehicles stale, and raises an immediate Dispatcher re-declare task. |
| [D24](../domain/decisions.md#d24), [D31](../domain/decisions.md#d31) | A stale goods list: the Dispatcher prepares a new one, the Receiver re-files it, and the goods are held at a registered hub near the border. |
| [O21](../domain/decisions.md#o21) | Tasks are shown **on screen**. No email. |

## Increments

### Increment 1: the snapshot and the pure comparison

- **Domain:** `LoadSnapshot` with a `Version`, the vehicle VIN, and per box its id, version (or a content hash), weight,
  Receiver, and items with category, quantity, value and code.
- `Staleness.IsStale(snapshot, currentLoad)` is a pure function.
- **Tests (RED first):** a unit table that mirrors [the staleness rules](../domain/customs-declarations.md#staleness)
  row by row.
  - **Stale** when: a box is added, removed, replaced or moved; a box's weight, items, value, category or Receiver
    changes; the vehicle is swapped or withdrawn; a Receiver stops being registered.
  - **Not stale** when: a box moves bay; a QR label is reissued; delivery progress is recorded; crew or accommodation
    changes.

### Increment 2: store the snapshot

- **Schema:** `dbo.Declaration` gains `SnapshotJson nvarchar(max) NULL` and `SnapshotVersion int NULL`, set when the
  declaration moves to *ready to file*.
- `POST …/declarations/{kind}/ready` builds the snapshot from the vehicle's current allocation
  ([plan 07](07-box-allocation-ferry.md)).
- **Tests:** integration tests for the snapshot round trip. A unit test that an **old `SnapshotVersion`** compares on
  the fields it has, so adding a field later does not make everything stale.

### Increment 3: staleness is visible

- Declaration reads compute `IsStale` against the current load, and report `Stale` in place of `Filed` or `Accepted`
  when it is true.
- **Do not write `Stale` to the database on read.** Writing it is part of withdrawal (Increment 5).
- **Tests:** component tests: allocate, mark ready, record a reference, move a box to another vehicle, and both
  vehicles' declarations read `Stale`.

### Increment 4: a Receiver losing registration

- The current-load builder includes each Receiver's status, so a Receiver moving out of `registered`
  ([plan 04](04-receiver-registration.md)) makes the declarations naming it stale.
- **Tests:** a unit test and a component test.

### Increment 5: the re-declare task, and withdrawing

- `GET /tasks?role=Dispatcher` (or `/convoys/{id}/tasks`) lists stale declarations, with the resolution that fits the
  instrument (see [What staleness does](../domain/customs-declarations.md#what-staleness-does)).
- `POST …/declarations/{id}/withdraw` moves a stale declaration to `Withdrawn`, keeping the record and its reference,
  and starts a new `Draft` for the same scope. For the ENS this is the supersede from plan 08.
- **Tests:** component tests for the task list and withdrawal. Integration tests that the unique "one non-withdrawn per
  scope" rule holds through withdrawal.

### Increment 6: web

- A stale badge on the declarations panel.
- A Dispatcher task list on screen, linked from the convoy overview.
- Tests: Vitest with MSW; a Playwright smoke test of the move-then-stale flow.

### Increment 7: BDD

- `Features/Declarations.feature`: moving a box between vehicles makes both vehicles' declarations stale and raises
  tasks; withdrawing and re-recording clears them.

## Retires and transitional

- **Transitional:** "declarations current" does not block departure yet. It becomes a blocking requirement in
  [plan 13](13-readiness-departure.md). A closed declaration ([plan 18](18-leader-checklist-progress.md)) will stop
  being compared.

## Docs to update

- `CLAUDE.md`: the declarations slice (snapshot and staleness), and the API list.
- `README.md`: endpoints.
- `docs/domain/key-concepts.md` § Documents.
- ADR 0005: an implementation note.
- Gotchas: stale is derived on read, and the snapshot is versioned.

## Risks

- **Over-eager staleness.** Every field added to the snapshot can make every filed declaration stale. Versioning, and
  comparing only the fields the snapshot holds, guard against it.
- **Read cost.** Computing staleness on read joins the allocation and items. At roughly ten vehicles a convoy
  ([O23](../domain/decisions.md#o23)) this is fine, so do not cache.

## Testing

The staleness table (Unit), snapshot persistence (Integration), the move flow (Component and BDD), and the web badge
and tasks.

## Verification

The standard gates, plus on the local stack: record GMR references for two vehicles, move a box from one to the other,
and see both declarations go stale and two tasks appear. Withdraw one and record a new reference, and its task clears.

## Sequencing

Straight after [plan 08](08-declarations-filing.md). Needed by plans [13](13-readiness-departure.md),
[16](16-box-replacement-label.md) and [18](18-leader-checklist-progress.md).

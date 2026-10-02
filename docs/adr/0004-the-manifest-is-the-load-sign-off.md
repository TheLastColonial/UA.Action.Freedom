# 4. The manifest is the load sign-off, and boxes belong to the truck-list entry

Date: 2026-10-02

## Status

Accepted. Not yet implemented. Amends [ADR 0001](0001-truck-list-as-a-table.md).

## Context

[ADR 0001](0001-truck-list-as-a-table.md) made the truck list a table and the manifest its child, so that one
vehicle on one convoy carries exactly one manifest. That fixed the *identity* of the manifest. It left the
manifest as the place where everything about a vehicle's journey collects, and since then it has grown to hold:

- the **cargo**, through `ManifestBox`;
- the **customs paperwork** for three authorities: the GMR stamp (`GmrSubmittedAt`), the ELO envelope and the
  recorded ENS;
- the **ferry booking** status;
- the **approval sign-off** an Administrator gives;
- the **delivery state**, through a ten-state `ManifestStatus` (`Created`, `Proposed`, `Rejected`, `Confirmed`,
  `Preparing`, `Ready`, `InTransit`, `Delivered`, `Lost`, `Returned`).

These change for different reasons, on different schedules, and are owned by different roles. A Dispatcher builds
the load, an Administrator signs it off, a Convoy Leader moves it, French customs refuse an envelope, and a box is
seized at a border. One enum cannot describe "approved, but the envelope is refused and one box was moved", so the
status has had to be bent to fit, and the freeze rule that came with it
(`Manifest.GmrSubmittedAt` blocks every edit) has a harder consequence than it first looks: **nothing may reopen a
confirmed manifest**, which is incompatible with a load that legitimately changes after approval
([decision D3](../domain/decisions.md#d3), [X3](../domain/decisions.md#x3)).

## Decision

### The manifest is the sign-off, plus the document pack

A manifest is **the Administrator's sign-off of one vehicle's load, and the document pack generated from that load
and its declarations** ([decision P6](../domain/decisions.md#p6)). It keeps its name, its identity from
ADR 0001 (`(ConvoyId, Vin)`, one per entry) and its place under the truck-list entry. Its lifecycle shrinks to
**proposed, then approved or rejected**.

It no longer holds customs paperwork, the ferry booking, cargo or delivery state.

### Everything else moves to where it is actually owned

| Moves from the manifest | To | Decision |
| --- | --- | --- |
| Cargo (`ManifestBox`) | **Box allocations** on the truck-list entry | [D10](../domain/decisions.md#d10) |
| GMR, ELO, ENS | **Declarations** per vehicle, [ADR 0005](0005-declarations-are-per-vehicle-with-derived-staleness.md) | [X6](../domain/decisions.md#x6) |
| Ferry booking | A **per-vehicle, outbound** booking on the truck-list entry | [P1](../domain/decisions.md#p1) |
| Delivery state | **Box and vehicle outcomes**, [ADR 0015](0015-box-and-vehicle-outcomes-and-convoy-closing.md) | [P5](../domain/decisions.md#p5) |

### The freeze applies to the declared snapshot, not to the load

Approval no longer makes the load immutable for ever. The load may change after sign-off, but **every change after
sign-off needs Administrator re-approval** ([decision X3](../domain/decisions.md#x3)), and it makes the vehicle's
declarations stale ([ADR 0005](0005-declarations-are-per-vehicle-with-derived-staleness.md)). Nothing carries on
describing a load that is no longer the load. This **reverses** the rule recorded in
[Manifest Status](../domain/key-concepts.md#manifest-status) that nothing may reopen a confirmed manifest.

`GmrSubmittedAt` stops being the freeze signal. A manifest edit is refused while the sign-off stands, and an edit
returns the manifest to *proposed*.

## Alternatives considered

**Keep the manifest as the central object**, and add the missing facts to it. One thing to look at, but every new
kind of fact makes it larger and its status more overloaded. This is the situation that produced the decision.

**A new `Dispatch Plan` or `Convoy Dossier` above the convoy.** It adds a layer that duplicates `Convoy`, which is
already the unit that is planned.

**Rename `Manifest` to `Load Sign-off`.** Clearer, but the printed document the Dispatcher hands a driver is still a
manifest, and renaming would break the vocabulary Ukrainian Action uses. The name stays and the definition changes
([decision P6](../domain/decisions.md#p6)).

## Consequences

**`ManifestStatus` is split, not trimmed.** `Created`, `Proposed`, `Rejected` and `Confirmed` stay with the
sign-off. `Preparing`, `Ready`, `InTransit`, `Delivered`, `Lost` and `Returned` describe boxes and vehicles, and
leave. `ManifestTransitions.CanTransition`, `docs/manifest-status.puml` and
`tests/UA.Action.Freedom.Tests.Unit/Domain/ManifestTransitionsTests.cs` all change with it. Where `Preparing` and
`Ready` belong is not settled: they are packing states, and the box lifecycle may absorb them.

**Arrival changes with it.** `POST /convoys/{id}/arrive` asks each vehicle for a manifest in `Delivered`, `Lost` or
`Returned`. It will ask boxes and vehicles for their outcomes instead
([ADR 0015](0015-box-and-vehicle-outcomes-and-convoy-closing.md)).

**The conditional-`UPDATE` discipline stays.** `ConfirmAndFreezeAsync` sets status and stamp in one statement so
the database settles a race. The same shape is needed for re-approval, because a window in which a load is changed
and its sign-off still stands is exactly what this ADR exists to remove.

**A migration is not needed.** The schema is end-state only and every stack is rebuilt from scratch.

**Two documents are now behind.** `docs/adr/0001` and the manifest sections of `docs/domain/key-concepts.md` and
`CLAUDE.md` describe the manifest as the central document and are amended or flagged in
[decisions](../domain/decisions.md#consequences-and-amendments-due).

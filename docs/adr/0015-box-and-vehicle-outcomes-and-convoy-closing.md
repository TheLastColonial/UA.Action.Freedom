# 15. Boxes and vehicles have explicit outcomes, and a convoy closes with a report

Date: 2026-10-02

## Status

Accepted. Not yet implemented. Supersedes the delivery half of `ManifestStatus`
([ADR 0004](0004-the-manifest-is-the-load-sign-off.md)).

## Context

How a load ends is currently one question asked of the manifest: its status, once it has left, is `Delivered`,
`Lost` or `Returned`. `POST /convoys/{id}/arrive` waits for every vehicle still travelling to have such a
manifest, then hands over `Delivered` and `Lost` vehicles for good (`Vehicle.HandedOverAt`).

That describes one outcome for one vehicle. Reality is finer and messier:

- A **box** is the thing delivered to a Receiver, and one vehicle's boxes can have different Receivers and
  different fates ([decision P5](../domain/decisions.md#p5)).
- **Customs can refuse a box at a border.** It may be seized, or it may go back to a hub.
- **A box can be damaged or stolen.** Cargo is not insured, so the question is not a claim but a status.
- **Arriving is not the same as being accepted.** Ukrainian customs accept a delivery under their own rules, and
  the charity needs to record that.
- **A convoy has to be accounted for.** What was expected, what was spent and what was delivered or not are
  questions the charity is asked, and today nothing answers them.

## Decision

### A box has one of a small set of outcomes

| Outcome | Meaning | Set by |
| --- | --- | --- |
| **Delivered** | Arrived at its destination | The Convoy Leader or the Dispatcher ([O8](../domain/decisions.md#o8)) |
| **Accepted** | Ukrainian customs have accepted the delivery | See [Q-accepted-granularity](../domain/decisions.md#q-accepted-granularity) ([O9](../domain/decisions.md#o9)) |
| **Seized** | Customs refused it and kept it | The Convoy Leader ([O2](../domain/decisions.md#o2)) |
| **Returned to a hub** | Customs refused it and it went back to a hub | The Convoy Leader ([O2](../domain/decisions.md#o2)) |
| **Undeliverable** | Damaged or stolen, with the reason | The Convoy Leader or Dispatcher ([O5](../domain/decisions.md#o5)) |

*Lost* is not a separate outcome. A box that is lost is undeliverable, with that as the reason.

*Seized* and *Undeliverable* are terminal. *Returned to a hub* puts the box back among the arrived boxes at that hub,
**with its label and contents unchanged**, so it can be allocated again
([Q-returned-box](../domain/decisions.md#q-returned-box)). A refused or undeliverable box **leaves the vehicle's
load**, which makes that vehicle's open declarations stale
([ADR 0005](0005-declarations-are-per-vehicle-with-derived-staleness.md)).

A photo to verify acceptance may be added later ([O9](../domain/decisions.md#o9)).

### A vehicle's outcome is delivery, and it is handed over

A vehicle is **delivered** when the Convoy Leader or the Dispatcher marks it arrived. A delivered vehicle is handed
over for good, as today (`HandedOverAt`), and is never offered for a convoy again. A **withdrawn** vehicle, one
that broke down and left, is unchanged from [ADR 0001](0001-truck-list-as-a-table.md).

### A convoy closes, and closing produces a report

Arrival is the journey ending. **Closing** follows it, and **generates a report of expected versus actual costs and
of the boxes delivered or not** ([decision O10](../domain/decisions.md#o10)). Costs come from the convoy's budget
lines and the actuals recorded against them. Reimbursing volunteers is **not** managed by the system.

The report is an account of the convoy, and it carries **no Receiver address and no delivery detail**: it is
aggregated, as the public value report is ([decision D29](../domain/decisions.md#d29)).

## Alternatives considered

**Keep one status on the manifest.** It cannot say that two boxes were delivered, one seized and one returned, and
that is the normal case once boxes have different Receivers.

**Make *Accepted* an Administrator or Ground Officer action.** Plausible: the Ground Officer is the one "ensuring
aid that is delivered is accepted", per their role. It is left open
([Q-accepted-granularity](../domain/decisions.md#q-accepted-granularity)) because it decides both who and at what
level.

**Auto-close on arrival.** Declined: there is a gap between arrival and a settled account, and the report is
meant to be generated deliberately.

## Consequences

**Arrival is asked of boxes and vehicles, not manifests.** `ArriveAsync` currently asks each vehicle for a finished
manifest. It will ask each vehicle and box for an outcome, and still take the convoy row first and never scan
`dbo.Vehicle` under a lock, which is the deadlock the current code was fixed for.

**Who closes a convoy, and whether closing locks it, is open**
([Q-close-convoy](../domain/decisions.md#q-close-convoy)). The working assumption is the Dispatcher, and yes.

**The value report and the close report should agree.** Both count delivered boxes and their value. They must use
the same definition of "delivered", and the closing report must not count a seized or undeliverable box as
delivered value.

**Existing statuses retire with the manifest's.** `Delivered`, `Lost` and `Returned` leave `ManifestStatus`
([ADR 0004](0004-the-manifest-is-the-load-sign-off.md)), and `manifest-status.puml` changes with it.

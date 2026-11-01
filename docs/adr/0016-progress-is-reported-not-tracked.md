# 16. Progress is reported by the Convoy Leader, and nothing is tracked

Date: 2026-10-02

## Status

Accepted. Implemented by [plan 18](../plans/18-leader-checklist-progress.md), on top of the route-point groundwork [plan 10](../plans/10-route-points-convoy-leader.md) built. See *Implementation notes* at the end.

## Context

HQ wants to know how a convoy is getting on. The obvious source is the vehicles' location. The project owner
declined that: **GPS tracking is not recommended, because of connectivity and security concerns**
([decision O20](../domain/decisions.md#o20)).

Both concerns are real. A convoy crosses areas with poor coverage, so a tracker produces gaps, false alarms and a
system that appears to know more than it does. And a **live feed of where a convoy is, stored by the charity, is
itself a sensitive asset**. It is exactly the kind of information that, if leaked, becomes a targeting aid. The
system's whole stance on Ukrainian delivery detail ([ADR 0009](0009-convoy-leader-reads-destination-addresses.md))
is to hold as little as possible, and a position history would sit uneasily with it.

## Decision

### There is no tracking

No GPS, no live location and no automatic detection of position. Nothing in the system knows where a vehicle is
unless a person says so.

### The Convoy Leader reports progress, point by point

The Convoy Leader, from the checklist page, **marks arrival at each route point and each accommodation**, marks
**borders crossed**, and enters fuel ([decisions O20, X2](../domain/decisions.md#o20)). HQ sees progress **from
those marks**. The granularity is a route point, not a position.

A crossing mark is also the event that **closes a declaration** at that border
([ADR 0005](0005-declarations-are-per-vehicle-with-derived-staleness.md)).

### The page is a web page and stores nothing on the phone

It is used over the internet on a phone with no app installed, and **nothing is stored on the device**
([decision X10](../domain/decisions.md#x10)). It requires sign-in and is sent with no-store cache headers.

### The fallback is a phone call and paper

If the page is unavailable, because there is no coverage or the device has failed, the leader **calls HQ and uses
printed documents** ([decision O18](../domain/decisions.md#o18)). HQ's knowledge of progress is therefore as good
as the leader's reports.

## Alternatives considered

**GPS tracking.** Rejected for the two reasons above.

**Periodic check-in calls only.** It is the fallback and it is not recorded. The marks are what give HQ a record
and drive the declarations.

**An offline-capable app.** It would cope with poor coverage, and it requires storing data on the device and an
app install. Both were declined ([decision X10](../domain/decisions.md#x10)).

## Consequences

**A forgotten mark has a cost.** If the leader does not mark a crossing, the declaration stays open, so a later
change to the load can still make it stale and raise a re-declare task after the border has really been crossed.
Whether a Dispatcher may record a mark on the leader's behalf, from a radio or phone report, is not decided
([Q-crossing-fallback](../domain/decisions.md#q-crossing-fallback)).

**A mark is made after the fact more often than it is made in the moment.** When coverage returns the leader enters
what happened. The system records when it was **entered**, and whether it also needs when it **happened** is open
([Q-occurrence-time](../domain/decisions.md#q-occurrence-time)).

**HQ's progress view is derived, not live.** It is the route's points with their marks, and nothing between them.
That is deliberate, and should be said plainly to anyone expecting a map.

**There is no position data to protect or retain.** Which is the point. What is held is a list of reached points
with who marked them and when ([ADR 0017](0017-every-entity-records-its-last-change.md)), and the retention of
that list is part of [Q-retention](../domain/decisions.md#q-retention).

## Amendments

Added 2026-10-02, from questions resolved before implementation planning.

- **A Dispatcher may record a mark or a crossing on the leader's behalf,** from a radio or phone report, and is named as
  the person who recorded it ([decision O27](../domain/decisions.md#o27)). That resolves the "forgotten mark" concern
  above.
- **Only the time of entry is recorded** ([decision O27](../domain/decisions.md#o27)).

## Implementation notes

Added with [plan 18](../plans/18-leader-checklist-progress.md).

- **A mark is a row of `dbo.RoutePointMark`:** the route point, `Reached` or `Crossed`, an optional vehicle, the person who entered it, the time it was **entered** (nothing else is kept, [O27](../domain/decisions.md#o27)) and whether that person was not the sitting leader. A mark is one fact, so a second one for the same point, kind and vehicle is refused, and it pins its route point (a marked point cannot be removed from the route).
- **A crossing closes the declarations of that border in the same transaction:** UK the GMR, EU the ENS and ELO, Ukraine the goods lists, for the vehicles still travelling, and only those that are `Accepted` ([ADR 0005](0005-declarations-are-per-vehicle-with-derived-staleness.md)).
- **The leader's routes are scoped** (`convoys:report-progress` with the convoy scope of [ADR 0010](0010-resource-scoped-permissions.md)). They enter **fuel only** among the costs.
- **"Nothing stored on the device" is enforced three ways:** every checklist response is `Cache-Control: no-store` (refusals included), the page's own code names no browser storage (a test reads the source), and the progress query is not kept in the query cache after the page closes.
- **Not done:** a mark does not say when it *happened* ([Q-occurrence-time](../domain/decisions.md#q-occurrence-time) was answered *entered only*), and the leader's access ending at convoy closing waits on plan 19.

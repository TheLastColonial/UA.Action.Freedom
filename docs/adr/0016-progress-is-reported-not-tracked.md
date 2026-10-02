# 16. Progress is reported by the Convoy Leader, and nothing is tracked

Date: 2026-10-02

## Status

Accepted. Not yet implemented.

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

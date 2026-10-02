# 12. Receiver registration gates convoys and boxes, and what a Receiver is stays out of the software

Date: 2026-10-02

## Status

Accepted. Not yet implemented.

## Context

A **Receiver** is the body a box or a vehicle goes to. Today `ReceiverReadModel` carries a reference, an
organisation and a region and nothing else, and the delivery address and contact sit in the `sensitive` schema
behind `receivers:detail`. A Receiver exists, or does not. Nothing says whether it is *entitled to receive*.

That matters because **Ukraine's goods list is filed by the Receiver, not by us**
([decision D20](../domain/decisions.md#d20)), and a Receiver that is not registered to do so cannot file it. A
convoy that arrives with boxes for such a Receiver has a load that cannot lawfully be declared
([research](../domain/ua-customs-requirements.md)). It is far better to refuse the allocation weeks earlier than to
discover it at the border.

Product discovery also drew a line the project owner was emphatic about: **who a Receiver is, and whether a vehicle
may lawfully go to them, is not for this software to know** ([decision D33](../domain/decisions.md#d33)). Receivers
are the most sensitive data in the system, and a field that says what kind of body one is would itself be a
sensitive fact.

## Decision

### A Receiver has a registration status, and only `registered` lets things proceed

The status is one of **pending, registered, suspended** or **expired**, and it is **managed by an Administrator**
([decisions D22, D30](../domain/decisions.md#d22)). Only a `registered` Receiver ([decision D35](../domain/decisions.md#d35)):

- lets a **box be assigned a destination**;
- lets a **vehicle's handover Receiver** be set, and counts towards departure
  ([P11](../domain/decisions.md#p11), [ADR 0008](0008-readiness-is-computed-and-blocking-rules-are-not-overridden.md));
- keeps the declarations that name it **current**. A Receiver ceasing to be `registered` makes them stale
  ([ADR 0005](0005-declarations-are-per-vehicle-with-derived-staleness.md)).

The Administrator is the **only actor**. Which transitions are legal is left to implementation.

### The software does not record what a Receiver is

There is **no field, status, document or test that describes the nature of a Receiver**. "Registered" means the
Administrator has recorded that it may be sent to, and nothing more: the *basis* for that, for example any
registration it holds in Ukraine's own system, is not stored. The research's suggestion to capture a Receiver's
registration identifiers was deliberately **not adopted** for the same reason
([research note](../domain/ua-customs-requirements.md#implications-for-box-and-item-data)).

### A distribution hub is a Location, registered by an Administrator

A hub is any `Location` an Administrator has registered, **with no status of its own**
([decision D36](../domain/decisions.md#d36)). Real places are data, never code or documentation. This is also where
goods wait when a goods list must be re-filed, at a registered hub near the border
([D32](../domain/decisions.md#d32)).

## Alternatives considered

**Record why a Receiver is registered** (an identifier, a reference to their registration). Useful for an audit,
and a sensitive fact in its own right. Declined, and revisited only with a data-sensitivity review.

**Gate only at departure.** Simpler, but a convoy could be planned and loaded around a Receiver that can never
receive it. Gating the *allocation* is what makes the problem visible early.

**Let a Ground Officer manage registration.** The Ground Officer already holds Receiver detail and
`receivers:write`. Declined: registration is an act of authorisation, as approving a volunteer is, and the
Administrator owns that kind of act. It also keeps the Ground Officer's isolation intact.

## Consequences

**Registration is narrower than `receivers:write`.** `receivers:write` is Administrator and Ground Officer today. A
status change is Administrator only, so it needs its own permission, or the status moves outside what
`receivers:write` can set.

**A registration can lapse.** `expired` implies a date. If a Receiver's registration carries an expiry, the warning
"expires before the expected delivery" ([P17](../domain/decisions.md#p17)) needs it, and nothing records one
today. Whether `expired` is set by hand or derived from a date is open.

**An existing allocation can be broken by a status change.** Suspending a Receiver after boxes were allocated to
them leaves those allocations pointing at a Receiver that no longer qualifies. They become blocking requirements
on their vehicles, which is the intended effect, and the Administrator should see which convoys a change touches.

**`ReceiverReadModel` stays small.** A status is not sensitive and sits beside the reference, organisation and
region, so a handler holding a Receiver still has nothing sensitive to leak.

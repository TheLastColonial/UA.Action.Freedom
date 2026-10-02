# 5. Customs declarations are one concept, per vehicle, and go stale when the load changes

Date: 2026-10-02

## Status

Accepted. Not yet implemented. Builds on [ADR 0004](0004-the-manifest-is-the-load-sign-off.md), and generalises
[ADR 0002](0002-elo-envelope-on-manifest-approval.md) and [ADR 0003](0003-ens-declaration-recorded-not-submitted.md).

## Context

Three authorities need a statement about a vehicle's load before it crosses their border, and each arrived as a
special case:

- the **GMR** (HMRC) is a stamp on the manifest, `GmrSubmittedAt`;
- the **ELO** (French customs) is a blob and a queue beside the manifest
  ([ADR 0002](0002-elo-envelope-on-manifest-approval.md));
- the **ENS** (ICS2) is a recorded MRN, also a blob ([ADR 0003](0003-ens-declaration-recorded-not-submitted.md));
- the **Ukrainian goods list** has no representation at all, and is filed by the receiving body, not by us.

They share a shape but not a model, and none of them can express the thing the charity most needs: **a load that
changes after it has been declared**. A box is replaced ([decision D3](../domain/decisions.md#d3)), a box moves to
another vehicle after a breakdown ([D13](../domain/decisions.md#d13)), customs refuse a box
([O2](../domain/decisions.md#o2)). Today the freeze rule simply forbids it. The business does not: without a
current declaration a vehicle cannot cross, and a stale one is a refusal at the border.

The authorities also correct a declaration differently. The GVMS API has update and delete operations. An ELO may
be modified only while open and unpaired and **cannot gain or lose a declaration** once created. An ENS has
non-amendable fields, so the normal correction is invalidate and refile. Ukraine has no known amendment route, and
the goods are held and a new list is filed ([research](../domain/ua-customs-requirements.md)).

## Decision

### One `Declaration`, per vehicle, for all four instruments

A declaration is a statement to a customs authority about **one vehicle's load**, because that is what is declared
at the border ([decision X6](../domain/decisions.md#x6)). The instruments differ in who files and how they are
corrected, and share one lifecycle:

`Draft` → `Ready to file` → `Filed` → `Accepted` (or `Refused`), then `Stale`, `Withdrawn` or `Closed`.

The full states and rules are in [Customs declarations](../domain/customs-declarations.md#lifecycle). The Ukrainian
goods list is **one per Receiver, per vehicle, per convoy**, so a vehicle with boxes for several Receivers has
several. How Ukrainian customs accept that is not known
([Q-multi-receiver-border](../domain/decisions.md#q-multi-receiver-border)), and the design does not assume they
will.

The ordering dependency from ADR 0003 is kept: **ENS, then ELO**, because an envelope cannot be created without the
MRN. The GMR is independent.

### Staleness is derived from a snapshot, never set by hand

When a declaration becomes *ready to file* it stores a **snapshot of what it was written from**: the boxes by
identity and version, their items, weights, values, categories and Receivers, and the vehicle. It is **stale**
whenever the current load differs. No one flags it, so no one can forget to. A stale declaration:

1. appears as **Stale** and raises an immediate **re-declare task** for the Dispatcher
   ([decision D13](../domain/decisions.md#d13));
2. fails the blocking requirement "declarations current", so the convoy cannot depart
   ([ADR 0008](0008-readiness-is-computed-and-blocking-rules-are-not-overridden.md)).

What does and does not make one stale is set out in
[Staleness](../domain/customs-declarations.md#staleness). A bay move or a label reissue does not. A box added,
removed, replaced, moved, refused or found undeliverable does.

### A declaration closes at the crossing

A declaration **closes** when the border it covers is crossed, and a closed one is frozen: after the crossing a
load change can no longer make it stale, because it has happened. The **Convoy Leader marks the crossing** from the
checklist page ([decision X2](../domain/decisions.md#x2), [ADR 0016](0016-progress-is-reported-not-tracked.md)).
A crossing is an event on a route point, not a stretch of the journey, and that is what replaces what a journey leg
would have recorded ([ADR 0007](0007-journey-legs-are-removed-from-the-crew-model.md)).

## Alternatives considered

**A "stale" flag set by whoever changes the load.** Simple, and wrong in the way that matters: it relies on every
code path that changes a load remembering to set it. A snapshot comparison fails safe.

**Keep three instruments as three models** and add staleness to each. It triples the work and leaves the Ukrainian
list, which has no model, as a fourth special case.

**One declaration per convoy.** Rejected: the border sees a vehicle, and the GMR, ENS and ELO are per vehicle
already ([decision X6](../domain/decisions.md#x6)).

## Consequences

**Four existing shapes fold into one.** `Manifest.GmrSubmittedAt`, the `elo/` blobs and queue messages, and the
`ens/{manifestId}.json` blob become instances of a declaration. The blob stores, the `IfNoneMatch` write-once rule,
the `IEnsDeclarationStore` seam and the ELO disposition table all survive, because they are properties of
particular authorities and not of the model.

**The snapshot has to be stored.** Whether it is the full content or an identity-and-version list plus a hash is an
implementation choice. It must be enough to show a Dispatcher *what changed*, not only *that* something did.

**An amendment path is new work** for the GMR (update or delete), the ELO (a new envelope against the new MRN), and
the ENS (invalidate, refile, keep the old MRN as history). Today all three are written once.

**A refused declaration keeps only a bounded reason code.** The authority's free text can quote the declaration it
objected to, so it is never stored or logged, which is the rule `EloEnvelopeProcessor` already follows.

**The Receiver's registration matters here too.** A Receiver ceasing to be `registered` makes the declarations that
name it stale ([ADR 0012](0012-receiver-registration-gates-convoys-and-boxes.md)).

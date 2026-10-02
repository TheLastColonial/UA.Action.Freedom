# 6. Filing is manual by default, and automatic submission is opt-in per authority

Date: 2026-10-02

## Status

Accepted. Not yet implemented. Amends [ADR 0002](0002-elo-envelope-on-manifest-approval.md), and generalises
[ADR 0003](0003-ens-declaration-recorded-not-submitted.md).

## Context

Approving a manifest today **automatically** hands off the paperwork. `ApproveManifestHandler` freezes the manifest
and then enqueues the GMR, the document pack and the ELO, each through `HandOff(stage, …)`. That design assumes
every authority's API is available and the charity's access to it is in place.

That assumption does not hold evenly:

- **Ukraine has no API we can use.** The receiving body files the goods list in Ukraine's own system, and no
  third-party route was found ([research](../domain/ua-customs-requirements.md)).
- **The ENS cannot be submitted by Freedom at all** ([ADR 0003](0003-ens-declaration-recorded-not-submitted.md)).
- **The GMR and ELO have clients**, but the project owner's position is that the APIs *will* be built and must not
  be *assumed* to be operating: until each is confirmed live and the charity's access is proven, the real filing
  is done by hand in the HMRC and French portals ([decision X5](../domain/decisions.md#x5)).

An automatic hand-off that fires on approval is therefore wrong in two ways. It acts before the declaration may be
ready, since a load can still change ([ADR 0005](0005-declarations-are-per-vehicle-with-derived-staleness.md)),
and it presumes an integration that may not be live.

## Decision

### Each authority has a submission mode

`manual` or `automatic`, **per authority**, set by configuration, with **`manual` the default**
([decisions X5, D4](../domain/decisions.md#x5)).

| Mode | What "filed" means | What the system does |
| --- | --- | --- |
| **Manual** | A Dispatcher **records the authority's reference** | Prepares the declaration, shows what to file, and records the reference. Nothing is sent. |
| **Automatic** | The system submitted it and the authority answered | Enqueues the submission as it does today, with the existing dispositions. |

The ENS is permanently manual, since there is no route to automate ([ADR 0003](0003-ens-declaration-recorded-not-submitted.md)).
Ukraine's goods list is permanently manual and is filed by the Receiver, outside the system, with the Dispatcher
recording the reference ([decision D20](../domain/decisions.md#d20)).

### Filing is an explicit act, not a side effect of approval

Approval signs off the load ([ADR 0004](0004-the-manifest-is-the-load-sign-off.md)). **Declarations are prepared
after sign-off, and filing is a separate, deliberate step.** In automatic mode that step is taken by the system
once a declaration is *ready to file*. It is never taken as a by-product of approving.

## Alternatives considered

**Automatic by default, with a manual override.** Rejected: the default would be wrong wherever the integration is
not proven, and the failure is silent. A manifest approved, a hand-off "successful", and nothing filed.

**A single global switch.** Rejected: the authorities differ, and the ENS and Ukraine cannot ever be automatic.

**Remove the automatic path until it is proven.** Rejected: the GMR and ELO clients, queues, dispositions and
tests exist and are valuable, and the local stack and the BDD suite exercise them. They are kept behind the mode.

## Consequences

**Approval stops enqueueing by default.** `ApproveManifestHandler`'s hand-offs run only where the authority's mode
is `automatic`. Counting and logging a failed hand-off, and the order "freeze, then enqueue", still hold in that
case.

**The local and test environments must say `automatic`.** The WireMock-backed stack and the BDD features assume the
hand-off happens on approval, and will configure the mode explicitly so they keep proving the automatic path.

**Recording a reference is a new write.** For the GMR and ELO it is currently produced by a worker. In manual mode
a Dispatcher enters the reference, so the declaration needs a "recorded by" and "recorded at", and the same
write-once discipline as the ENS MRN: a conflict is reported, not overwritten.

**Moving an authority to automatic is a configuration change with an operational meaning.** It should be done
deliberately, and an authority that fails to answer in automatic mode should be visible, as the dead-letter counters
already make it.

**The dashboards still describe the automatic path.** `GmrSubmissionProcessor`, `EloEnvelopeProcessor` and the
Grafana panels keep their meaning and will simply show little in manual mode.

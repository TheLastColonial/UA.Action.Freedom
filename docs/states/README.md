# State diagrams

PlantUML state diagrams of the lifecycles in the domain: what states a thing can be in, and the only ways it can move
between them. They show the **target design**. The transition rules they draw are the ones the
[implementation plans](../plans/README.md) pin edge by edge in unit tests, the same way
`ManifestTransitionsTests` pins the manifest today.

| Diagram | Lifecycle | Rules | Plans |
|---|---|---|---|
| [Box lifecycle](box-lifecycle.puml) | Expected, Arrived, Attested, Allocated, Departed, then Delivered and Accepted, Seized, Returned to hub, Undeliverable; Void when replaced | [Boxes and donations](../domain/boxes-and-donations.md#box-lifecycle) | [05](../plans/05-item-classification-value.md), [07](../plans/07-box-allocation-ferry.md), [14](../plans/14-outcomes-closing.md), [16](../plans/16-box-replacement-label.md) |
| [Convoy lifecycle](convoy-lifecycle.puml) | Planning (truck list open, then published), Departed, Arrived, Closed | [Convoy operations](../domain/convoy-operations.md#convoy) | [13](../plans/13-readiness-departure.md), [14](../plans/14-outcomes-closing.md) |
| [Truck-list entry](truck-list-entry.puml) | A vehicle on a convoy: on the list, travelling, delivered, handed over; or withdrawn | [Convoy operations](../domain/convoy-operations.md#truck-list-entry) | [07](../plans/07-box-allocation-ferry.md), [13](../plans/13-readiness-departure.md), [14](../plans/14-outcomes-closing.md) |
| [Manifest status](../manifest-status.puml) | The load sign-off: Proposed, Approved, Rejected | [Convoy operations](../domain/convoy-operations.md#manifest-the-load-sign-off) | [08](../plans/08-declarations-filing.md), [15](../plans/15-manifest-signoff-lifecycle.md) |
| [Declaration lifecycle](declaration-lifecycle.puml) | Draft, Ready to file, Filed, Accepted or Refused, Stale, Withdrawn, Closed | [Customs declarations](../domain/customs-declarations.md#lifecycle) | [08](../plans/08-declarations-filing.md), [09](../plans/09-declaration-staleness.md), [18](../plans/18-leader-checklist-progress.md) |
| [Receiver registration](receiver-registration.puml) | Pending, Registered, Suspended, Expired | [Boxes and donations](../domain/boxes-and-donations.md#receivers-and-destinations) | [04](../plans/04-receiver-registration.md) |

Related: [process diagrams](../process/README.md), [sequence diagrams](../sequences/README.md),
[domain model](../model/domain-model.puml).

Render with `docker run --rm -v "$PWD":/data plantuml/plantuml -tsvg /data/*.puml` (on Git Bash, prefix
`MSYS_NO_PATHCONV=1` and use `"$(pwd -W)"`).

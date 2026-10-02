# Process diagrams

PlantUML activity diagrams of the business processes introduced by [ADRs 0004 to 0017](../adr/README.md), with a
swimlane per role. They show **who does what, in what order, and where it can stop**. Their companions in
[`docs/sequences/`](../sequences/README.md) show the same flows as calls between people and systems, and use the same
numbering.

They show the **target design**; most of it is not built yet. The [implementation plans](../plans/README.md) build it.
Where a diagram and a [domain document](../domain/README.md) disagree, the domain document wins.

| # | Process | Who takes part | Sequence | Plans |
|---|---|---|---|---|
| 00 | [End-to-end overview](00-end-to-end-overview.puml) | Donor, Loader, Dispatcher, Administrator, Convoy Leader | – | all |
| 01 | [Login and attribution](01-login-and-attribution.puml) | Volunteer, Administrator | [01](../sequences/01-login-and-attribution.puml) | [02](../plans/02-login-person-link.md), [03](../plans/03-last-changed-audit.md) |
| 02 | [Donation and box intake](02-donation-and-box-intake.puml) | Donor, Dispatcher, Loader | [02](../sequences/02-donation-and-box-intake.puml) | [04](../plans/04-receiver-registration.md), [05](../plans/05-item-classification-value.md), [06](../plans/06-donors-donations.md), [16](../plans/16-box-replacement-label.md) |
| 03 | [Box replacement](03-box-replacement.puml) | Loader, Administrator, Dispatcher | [03](../sequences/03-box-replacement.puml) | [16](../plans/16-box-replacement-label.md) |
| 04 | [Convoy planning](04-convoy-planning.puml) | Dispatcher, Mechanic | [04](../sequences/04-convoy-planning.puml) | [01](../plans/01-crew-without-legs.md), [04](../plans/04-receiver-registration.md), [07](../plans/07-box-allocation-ferry.md), [10](../plans/10-route-points-convoy-leader.md), [11](../plans/11-accommodation.md), [12](../plans/12-budget-equipment.md) |
| 05 | [Load sign-off and declarations](05-load-signoff-and-declarations.puml) | Dispatcher, Administrator, Ground Officer, Receiver, Customs Worker | [05](../sequences/05-load-signoff-and-declarations.puml) | [08](../plans/08-declarations-filing.md) |
| 06 | [Load change and re-declare](06-load-change-and-redeclare.puml) | Dispatcher, Administrator, Loader | [06](../sequences/06-load-change-and-redeclare.puml) | [09](../plans/09-declaration-staleness.md), [15](../plans/15-manifest-signoff-lifecycle.md) |
| 07 | [Departure](07-departure.puml) | Dispatcher | [07](../sequences/07-departure.puml) | [13](../plans/13-readiness-departure.md) |
| 08 | [On the road](08-on-the-road.puml) | Convoy Leader, Dispatcher | [08](../sequences/08-on-the-road.puml) | [14](../plans/14-outcomes-closing.md), [18](../plans/18-leader-checklist-progress.md) |
| 09 | [Delivery, acceptance and closing](09-delivery-acceptance-closing.puml) | Convoy Leader, Receiver, Dispatcher | [09](../sequences/09-delivery-acceptance-closing.puml) | [14](../plans/14-outcomes-closing.md), [15](../plans/15-manifest-signoff-lifecycle.md) |
| 10 | [Leader address access](10-leader-address-access.puml) | Convoy Leader, Ground Officer | [10](../sequences/10-leader-address-access.puml) | [17](../plans/17-scoped-permissions.md), [19](../plans/19-leader-address-access.md) |
| 11 | [Receiver registration](11-receiver-registration.puml) | Ground Officer, Administrator, Dispatcher | [11](../sequences/11-receiver-registration.puml) | [04](../plans/04-receiver-registration.md) |

## Goods movements, and the older top-level diagrams

[`goods-movements.puml`](goods-movements.puml) and [`goods-movements.md`](goods-movements.md) trace one box from
attestation through allocation, sign-off and declarations, as a sequence. Like [`../process.puml`](../process.puml) and
[`../manifest-status.puml`](../manifest-status.puml), they now show the **target design**. The code still runs the
earlier flow (approval freezes the manifest and hands off the paperwork) until [plan 08](../plans/08-declarations-filing.md)
and [plan 15](../plans/15-manifest-signoff-lifecycle.md) merge. The as-built versions are in git history at `f659eed`.

Related: [state diagrams](../states/README.md), [use case diagrams](../use-cases/README.md),
[domain model](../model/domain-model.puml), [convoy timeline](../timeline/convoy-timeline.puml).

## Rendering

```bash
cd docs/process
docker run --rm -v "$PWD":/data plantuml/plantuml -tsvg /data/*.puml
```

On Windows Git Bash, prefix that with `MSYS_NO_PATHCONV=1` and use `"$(pwd -W)"` for the volume. The `.puml` files are
the source; images are not committed.

## Keeping them current

When a plan changes a process drawn here, the same PR updates the diagram, as it would a domain document.

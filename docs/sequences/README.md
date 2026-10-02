# Sequence diagrams

PlantUML sequence diagrams of the flows introduced by [ADRs 0004 to 0017](../adr/README.md). They show the **target
design**, so most of what they draw is not built yet. The [implementation plans](../plans/README.md) build it.

Each has a companion **process diagram** with the same number in [`docs/process/`](../process/README.md), which shows
the same flow role by role.

Each diagram's caption names the [decisions](../domain/decisions.md) it illustrates. The diagrams illustrate the rules;
where a diagram and a domain document disagree, the [domain document](../domain/README.md) wins.

| # | Diagram | Shows | Rules | Plans |
|---|---|---|---|---|
| 01 | [Login and attribution](01-login-and-attribution.puml) | Linking a login to a person, 403 for an unlinked login, recording who changed what, erasure removing the link | [O34](../domain/decisions.md#o34), [O35](../domain/decisions.md#o35), [O19](../domain/decisions.md#o19) | [02](../plans/02-login-person-link.md), [03](../plans/03-last-changed-audit.md) |
| 02 | [Donation and box intake](02-donation-and-box-intake.puml) | A donation announced and entered, items classified and valued, expiry and not-carried warnings, attestation, the bilingual label | [Boxes and donations](../domain/boxes-and-donations.md) | [05](../plans/05-item-classification-value.md), [06](../plans/06-donors-donations.md), [16](../plans/16-box-replacement-label.md) |
| 03 | [Box replacement](03-box-replacement.puml) | An attested box is voided and replaced, never edited or deleted | [D3](../domain/decisions.md#d3), [X3](../domain/decisions.md#x3) | [16](../plans/16-box-replacement-label.md) |
| 04 | [Convoy planning](04-convoy-planning.puml) | Route points, truck list, budget and equipment steps, crew, leader, insurance, ferry, handover Receivers, accommodation | [Convoy operations](../domain/convoy-operations.md) | [01](../plans/01-crew-without-legs.md), [04](../plans/04-receiver-registration.md), [07](../plans/07-box-allocation-ferry.md), [10](../plans/10-route-points-convoy-leader.md), [11](../plans/11-accommodation.md), [12](../plans/12-budget-equipment.md) |
| 05 | [Load sign-off and declarations](05-load-signoff-and-declarations.puml) | Approval as sign-off only; ENS, ELO, GMR and the Ukrainian goods list, in manual and automatic modes | [Customs declarations](../domain/customs-declarations.md) | [08](../plans/08-declarations-filing.md) |
| 06 | [Load change and re-declare](06-load-change-and-redeclare.puml) | A box moves; manifests reopen, declarations go stale, tasks appear, each instrument is corrected | [Staleness](../domain/customs-declarations.md#staleness) | [09](../plans/09-declaration-staleness.md), [15](../plans/15-manifest-signoff-lifecycle.md) |
| 07 | [Departure](07-departure.puml) | One departure action, refused until every blocking requirement holds, with no override | [Readiness](../domain/convoy-operations.md#readiness) | [13](../plans/13-readiness-departure.md) |
| 08 | [On the road](08-on-the-road.puml) | Progress marks, crossings closing declarations, the Dispatcher on the leader's behalf, fuel, border refusals | [O20](../domain/decisions.md#o20), [O27](../domain/decisions.md#o27), [O2](../domain/decisions.md#o2) | [14](../plans/14-outcomes-closing.md), [18](../plans/18-leader-checklist-progress.md) |
| 09 | [Delivery, acceptance and closing](09-delivery-acceptance-closing.puml) | Delivery, acceptance per goods list, arrival and handover, closing and the regenerable report | [O8](../domain/decisions.md#o8), [O24](../domain/decisions.md#o24), [O25](../domain/decisions.md#o25) | [14](../plans/14-outcomes-closing.md), [15](../plans/15-manifest-signoff-lifecycle.md) |
| 10 | [Leader address access](10-leader-address-access.puml) | Headers before the window, re-authentication, the audited read through the sensitive path, refusals | [X7](../domain/decisions.md#x7) to [X13](../domain/decisions.md#x13), [O26](../domain/decisions.md#o26) | [17](../plans/17-scoped-permissions.md), [19](../plans/19-leader-address-access.md) |
| 11 | [Receiver registration](11-receiver-registration.puml) | Registration gating destinations and departure; suspension and its effects | [D30](../domain/decisions.md#d30), [D33](../domain/decisions.md#d33), [D35](../domain/decisions.md#d35) | [04](../plans/04-receiver-registration.md) |

## Rendering

Any PlantUML renderer works, including the IDE plugins. Without installing anything, Docker will do:

```bash
cd docs/sequences
docker run --rm -v "$PWD":/data plantuml/plantuml -tsvg /data/*.puml
```

On Windows Git Bash, prefix that with `MSYS_NO_PATHCONV=1` and use `"$(pwd -W)"` for the volume, or the path is
rewritten. The images are named after each diagram's `@startuml` title. They are not committed; the `.puml` files are
the source.

## Keeping them current

When a plan changes a flow drawn here, the same PR updates the diagram, as it would a domain document.

# Architecture decision records

Why the system is built the way it is. Each record states its context, the decision, what was rejected, and what
follows. The business rules are in [`docs/domain/`](../domain/README.md), and every decision there links to the ADR
that records it. See [the decision-to-ADR map](../domain/decisions.md#architecture-decision-records).

## Index

| # | Title | Status |
|---|---|---|
| [0001](0001-truck-list-as-a-table.md) | The truck list is a table, and the manifest is its child | Accepted. **Partly superseded** by 0004 and 0007. |
| [0002](0002-elo-envelope-on-manifest-approval.md) | The French logistics envelope hangs off the manifest and is requested by approving it | Accepted. **Partly superseded** by 0005 and 0006. |
| [0003](0003-ens-declaration-recorded-not-submitted.md) | The ICS2 declaration is recorded, not submitted, and approval will not proceed without one | Accepted. **Amended** (who files it). |
| [0004](0004-the-manifest-is-the-load-sign-off.md) | The manifest is the load sign-off, and boxes belong to the truck-list entry | Accepted |
| [0005](0005-declarations-are-per-vehicle-with-derived-staleness.md) | Customs declarations are one concept, per vehicle, and go stale when the load changes | Accepted |
| [0006](0006-filing-is-manual-by-default.md) | Filing is manual by default, and automatic submission is opt-in per authority | Accepted |
| [0007](0007-journey-legs-are-removed-from-the-crew-model.md) | Journey legs are removed from the crew model | Accepted |
| [0008](0008-readiness-is-computed-and-blocking-rules-are-not-overridden.md) | Readiness is computed from the facts, and blocking requirements are never overridden | Accepted |
| [0009](0009-convoy-leader-reads-destination-addresses.md) | The Convoy Leader may read destination addresses, scoped, time-limited and audited | Accepted. **Needs a security review before implementation.** |
| [0010](0010-resource-scoped-permissions.md) | Permissions can be scoped to a resource | **Proposed.** Becomes Accepted at the decision checkpoint that opens [plan 17](../plans/17-scoped-permissions.md). |
| [0011](0011-attested-boxes-are-replaced-not-edited.md) | An attested box is replaced, never edited, and its label attests the contents | Accepted. Gated by a translation spike and a label data-sensitivity review ([plan 16](../plans/16-box-replacement-label.md)). |
| [0012](0012-receiver-registration-gates-convoys-and-boxes.md) | Receiver registration gates convoys and boxes, and what a Receiver is stays out of the software | Accepted |
| [0013](0013-donors-are-a-split-identity.md) | Donors are a split identity, so they can be erased, and a donation is its own entity | Accepted |
| [0014](0014-items-are-classified-by-category-and-valued-in-gbp.md) | Items are classified by category, which maps to customs codes, and valued in GBP with a recorded source | Accepted |
| [0015](0015-box-and-vehicle-outcomes-and-convoy-closing.md) | Boxes and vehicles have explicit outcomes, and a convoy closes with a report | Accepted |
| [0016](0016-progress-is-reported-not-tracked.md) | Progress is reported by the Convoy Leader, and nothing is tracked | Accepted |
| [0017](0017-every-entity-records-its-last-change.md) | Every entity records who last changed it, and when | Accepted |

**Accepted** means the project owner decided it in product discovery. **Not yet implemented** applies to 0004 to
0017: they describe the intended system, and the code and some documents are behind. Where an ADR changes an
earlier one, the earlier ADR keeps its original text and gains an **Amendments** section, so the reasoning that was
true at the time is not lost.

## Implementation

ADRs 0004 to 0017 are implemented by the plan series in [`docs/plans/`](../plans/README.md), one branch and one PR per
plan. The plans index shows which plan covers which ADR, and each plan's status.

## Format

`# N. Title`, then **Date**, **Status**, **Context**, **Decision** and **Consequences**, with **Alternatives
considered** where a real alternative was weighed. Numbers are never reused.

# Goods movement: box attestation to declarations

> **Partly built.** [Plan 08](../plans/08-declarations-filing.md) is built: approval signs off the load and files
> nothing, a vehicle's declarations are recorded (manual, the default) or filed (automatic), and an ELO needs an
> accepted ENS. Still the target: the three-state manifest ([plan 15](../plans/15-manifest-signoff-lifecycle.md)),
> staleness from a snapshot ([plan 09](../plans/09-declaration-staleness.md)) and the departure gate
> ([plan 13](../plans/13-readiness-departure.md)). The earlier flow is in git history at commit `f659eed`.

The sequence is drawn in [`goods-movements.puml`](goods-movements.puml). The role-by-role processes are
[05 Load sign-off and declarations](05-load-signoff-and-declarations.puml) and
[06 Load change and re-declare](06-load-change-and-redeclare.puml). The rules are in
[Customs declarations](../domain/customs-declarations.md).

## Flow summary

### 1. Box attestation

- A Loader attests a box: `POST /boxes/{id}/validate`. The signer is taken **from the login**, never the request body.
- `ValidatedAt` and `ValidatedByPersonId` are set by a conditional update, once.
- Contents are now fixed. A change means **replacing** the box: the old one is voided and its QR code revoked
  ([ADR 0011](../adr/0011-attested-boxes-are-replaced-not-edited.md)).

### 2. Allocation to a vehicle

- The Dispatcher allocates the box to a vehicle on the truck list:
  `PUT /convoys/{id}/vehicles/{vin}/boxes/{boxId}`.
- A box is on at most one vehicle. Its Receiver must be registered.

### 3. Load sign-off

- The manifest is proposed, then an Administrator approves it: `POST /manifests/{id}/approve`.
- **Approval signs off the load and files nothing**
  ([ADR 0004](../adr/0004-the-manifest-is-the-load-sign-off.md)).
- A later load change returns the manifest to Proposed for re-approval.

### 4. Declarations, per vehicle

- The Dispatcher marks each declaration ready to file, which stores a **snapshot** of the load.
- **ENS:** filed by the Dispatcher in the EU Customs Trader Portal, with the Ground Officer entering the consignee
  address. The filing sheet never carries it. The MRN is recorded, write-once.
- **ELO:** needs an accepted ENS. Manual by default (record the reference). In automatic mode it is enqueued on
  `elo-envelopes` and created by the Customs Worker.
- **GMR:** manual by default. In automatic mode it is enqueued on `customs-work`.
- **Ukrainian goods list:** prepared per Receiver, filed by the Receiver in Ukraine, and its reference recorded.

### 5. When the load changes

- Moving, replacing, refusing or removing a box makes the affected declarations **stale** (derived from the
  snapshot), raises a re-declare task, and blocks departure until it is resolved.
- A declaration closed at its border crossing is no longer compared.

## Constraints and invariants

| Invariant | Enforcement |
|---|---|
| An attested box is never edited or deleted | Conditional stamp; replacement instead of edit; delete refused |
| A box is on at most one vehicle | Allocation keyed on `BoxId` |
| Approval files nothing | `ApproveManifestHandler` only signs off ([ADR 0006](../adr/0006-filing-is-manual-by-default.md)) |
| ELO needs the ENS MRN | Filing refused unless the ENS is Accepted |
| The ENS MRN is write-once | Blob created with `IfNoneMatch = ETag.All`; correction is withdraw and refile |
| No delivery address leaves the system | The filing sheet withholds it; workers have no database; the label has no receiver parameter |
| Staleness cannot be forgotten | Derived on read from the snapshot, never set by hand |

## Gotchas

- **Automatic "Filed" means enqueued.** The workers have no database, so in automatic mode a declaration is stamped
  Filed when the message is enqueued, not when the authority answers.
- **Creating an ELO is not idempotent.** A retry after a successful create makes a second envelope. The disposition
  rules in [ADR 0002](../adr/0002-elo-envelope-on-manifest-approval.md) still apply.
- **The document that travels with the vehicle is plain text** on purpose: deterministic and testable.

# 08. Declarations per vehicle, and manual filing by default

| | |
|---|---|
| **Branch** | `feat/declarations-filing` |
| **Covers** | [ADR 0005](../adr/0005-declarations-are-per-vehicle-with-derived-staleness.md) (entity and lifecycle; staleness is [plan 09](09-declaration-staleness.md)); [ADR 0006](../adr/0006-filing-is-manual-by-default.md); the "approval only signs off" part of [ADR 0004](../adr/0004-the-manifest-is-the-load-sign-off.md); amendments to ADRs [0002](../adr/0002-elo-envelope-on-manifest-approval.md) and [0003](../adr/0003-ens-declaration-recorded-not-submitted.md); [X5](../domain/decisions.md#x5), [X6](../domain/decisions.md#x6), [D4](../domain/decisions.md#d4), [D12](../domain/decisions.md#d12), [D20](../domain/decisions.md#d20), [X1](../domain/decisions.md#x1), [O32](../domain/decisions.md#o32) |
| **Depends on** | [04](04-receiver-registration.md), [05](05-item-classification-value.md), [07](07-box-allocation-ferry.md) |
| **Gate** | None |

## Context

Today the paperwork has four different shapes, all keyed by **manifest**:

- **GMR:** `Manifest.GmrSubmittedAt` is stamped at **approval** by `ConfirmAndFreezeAsync`
  (`ManifestRepository.cs` l.139), not when HMRC answers. The worker writes results to `gmr/{gmrId}.json` and never to
  the database.
- **ENS:** `ens/{manifestId}.json`, written once with `IfNoneMatch` by `Api/Documents/BlobEnsDeclarationStore.cs`.
  `Application/Manifests/ManifestEnsUseCases.cs` handles it. The filing sheet is in `ManifestEnsFilingUseCases.cs`.
- **ELO:** `elo/{id}.json` and `.pdf`, written by `CustomsWorker/Elo/EloEnvelopeProcessor.cs` from the `elo-envelopes`
  queue, and read through `IEloEnvelopeStore`.
- **Ukrainian goods list:** not modelled.

`ApproveManifestHandler` (`ManifestTransitionUseCases.cs` l.163):
1. refuses with `EnsNotFiled`;
2. freezes;
3. then **unconditionally** calls `HandOff("gmr")`, `HandOff("document")` and `HandOff("elo")`.

There is **no** feature flag or per-authority mode anywhere. The workers have no database, by design.

Rules: [Customs declarations](../domain/customs-declarations.md).

### Decisions this plan relies on

| Decision | Effect here |
|---|---|
| [X6](../domain/decisions.md#x6) | One `Declaration` per vehicle per instrument. The Ukrainian goods list is one per Receiver per vehicle per convoy. |
| [X5](../domain/decisions.md#x5), [D4](../domain/decisions.md#d4) | A `SubmissionMode` per authority, **manual by default**. In manual mode, "filed" means a Dispatcher records the reference. |
| [D12](../domain/decisions.md#d12), [D20](../domain/decisions.md#d20) | The Dispatcher files the UK and French declarations. The Receiver files the goods list outside the system, and the Dispatcher records its reference. |
| [X1](../domain/decisions.md#x1), [O32](../domain/decisions.md#o32) | The filing sheet still withholds the consignee address. The Ground Officer enters it in the portal. |
| ADR 0005 | Order is ENS then ELO: an ELO is refused without an accepted ENS. |

## Increments

### Increment 1: the declaration lifecycle

- **Domain:** a new `Declaration` (kind `Gmr | Ens | Elo | GoodsList`, scope `(ConvoyId, Vin, ReceiverRef?)`, status,
  reference, recorded by and at).
- The transitions are held as data, like `ManifestTransitions`:
  - `Draft → ReadyToFile → Filed → Accepted | Refused`;
  - `Refused → Draft`;
  - `Filed | Accepted → Stale`;
  - `Stale → Withdrawn → Draft`;
  - `Accepted → Closed`.
- For the ENS, `Filed` and `Accepted` collapse. **Stale** is set only by [plan 09](09-declaration-staleness.md).
  **Closed** is set only by a crossing ([plan 18](18-leader-checklist-progress.md)). This plan defines both and never
  sets them.
- **Tests (RED first):** a unit test per edge, and per refused edge, in the style of `ManifestTransitionsTests.cs`.

### Increment 2: schema and repository

- **Schema:** `dbo.Declaration (Id, ConvoyId, Vin, ReceiverRef NULL, Kind, Status, Reference NULL, ReasonCode NULL,
  RecordedBy, RecordedAt, LastChangedBy, LastChangedAt)`.
  - A foreign key to `ConvoyVehicle`.
  - A unique index on "one non-withdrawn declaration per scope and kind".
  - Status changes are conditional `UPDATE … WHERE Status = @from`.
- **Application:** `Application/Declarations/` with its port. **Data:** `Data/Declarations/DeclarationRepository.cs`.
- **Tests:** integration tests for conditional transitions and the unique rule. Add an in-memory fake.

### Increment 3: the ENS moves into the declaration, keeping write-once

- `RecordEnsDeclarationHandler` and `SupersedeEnsDeclarationHandler` move to `Application/Declarations/`, and record
  against the vehicle's ENS declaration.
- The blob moves from `ens/{manifestId}.json` to `declarations/{declarationId}.json`, with the same
  `IfNoneMatch = ETag.All` write-once rule.
- **Superseding** withdraws the declaration and keeps the old MRN as history (copy, then delete, as today).
- `IEnsDeclarationStore` stays the ITSP seam.
- **Tests:**
  - `tests/UA.Action.Freedom.Tests.Integration/Manifests/EnsDeclarationStoreTests.cs` moves and keeps proving the
    storage-level conflict.
  - Update the component tests and `InMemoryEnsDeclarationStore`.
- **Routes:** `PUT|DELETE /convoys/{id}/vehicles/{vin}/declarations/ens` under `manifests:declare`. The old
  `/manifests/{id}/ens` routes return 410, removed in [plan 15](15-manifest-signoff-lifecycle.md).

### Increment 4: manual "record reference" for every kind

- `POST /convoys/{id}/vehicles/{vin}/declarations/{kind}/record`, under `manifests:declare`, with `{ reference,
  receiverRef? }` (the receiver only for `GoodsList`).
- It moves the declaration to `Filed`, or to `Accepted` for the ENS, and is **write-once**: a conflict is reported,
  not overwritten.
- `POST …/refused` with a **bounded reason code only**. Never store the authority's free text.
- The **ELO is refused** unless the vehicle has an accepted ENS.
- **Tests:** unit, component and integration tests for each kind, the ELO-without-ENS refusal, and the write-once
  conflict.

### Increment 5: submission mode, manual by default

- **Configuration:** a `CustomsOptions.SubmissionMode` per authority (`Gmr`, `Elo`), defaulting to `Manual`, bound
  from environment variables in `src/UA.Action.Freedom.Api/Program.cs`. The ENS and goods list are always manual.
- In **automatic** mode, `POST …/declarations/{kind}/file` enqueues through the existing `IManifestWorkQueue` messages
  (`GmrSubmissionRequest`, `EloEnvelopeRequest`, `AzureManifestWorkQueue`) and moves the declaration to `Filed`. In
  manual mode the same route returns 409 "record the reference instead".
- **Tests:** component tests with the fake queue (`FreedomApi.cs` l.143), for automatic (enqueued) and manual (nothing
  enqueued).
- **Compose:** set `automatic` explicitly for the API in `iac/local/docker-compose.yml`, so the local stack and BDD keep
  proving the automatic path.

### Increment 6: approval only signs off

- `ApproveManifestHandler`:
  - **remove the `EnsNotFiled` gate** and the three `HandOff` calls;
  - **keep** `ConfirmAndFreezeAsync` (still the freeze until plan 15);
  - the manifest document request (`HandOff("document")`) moves to an explicit `POST /manifests/{id}/document`, or into
    the GMR file action. Pick one and record it in the PR.
- `FreedomMetrics.ApproveFailedAfterFreeze` is no longer emitted from approval. The equivalent count moves to the file
  action.
- **Tests:** rewrite `ApproveManifestHandlerTests.cs` and the BDD approval scenarios in `Manifests.feature` and
  `BoxDelivery.feature`.

### Increment 7: the filing sheet and the declarations panel

- The filing sheet route moves to `GET /convoys/{id}/vehicles/{vin}/declarations/ens/filing-sheet`.
- It still withholds the address, and its `ConsigneeAddressSource` text now says **"entered by the Ground Officer in
  the portal"** ([O32](../domain/decisions.md#o32)).
- **Web:** a declarations panel per vehicle (one row per kind, plus a row per Receiver for goods lists) with record,
  refused and file actions. It replaces `web/src/pages/manifests/ManifestEnsPanel.tsx`. Update `transitions.ts` so
  `approve` no longer implies paperwork.
- **Tests:** a component test that the filing sheet has no address. Vitest with MSW for the panel.

### Increment 8: BDD

- Rewrite `EnsSteps.cs` and `EloSteps.cs` around the vehicle's declarations.
- Scenarios:
  - in manual mode, approval enqueues nothing and the Dispatcher records the GMR reference;
  - in automatic mode (the compose default), filing the ELO produces an envelope as today;
  - an ELO without an ENS is refused.

## Retires and transitional

- **Retires:**
  - the `EnsNotFiled` gate on approval;
  - the unconditional hand-off on approval;
  - `ens/{manifestId}.json` keys;
  - `ManifestEnsPanel`.
- **Transitional:**
  - `GmrSubmittedAt` remains the freeze signal until [plan 15](15-manifest-signoff-lifecycle.md);
  - the old `/manifests/{id}/ens*` routes return 410 until then;
  - in automatic mode, `Filed` is stamped when the message is enqueued, because the workers have no database. Record
    this as a gotcha.

## Docs to update

- `CLAUDE.md`: the Manifests slice, approval, the declarations slice, `SubmissionMode`, the API list, and the "approval
  hands off three things" paragraph.
- `README.md`: endpoints and the local environment variable.
- `iac/README.md`: the submission mode variable.
- `docs/domain/key-concepts.md` § Documents.
- Gotchas: automatic "Filed" means enqueued.
- ADRs 0005 and 0006: implementation notes.
- `docs/domain/decisions.md`: the amendments table.

## Risks

- **This is the widest change in the series.** Keep the eight increments as separate commits, with every gate green
  between them.
- **Blob re-keying** touches health checks and the BDD steps. Grep for `ens/` and `elo/`.
- **The address must never leak.** Re-run the filing-sheet absence test after every change to it.

## Testing

All four .NET layers, the web panel, BDD in both modes, and the queue-fake component tests.

## Verification

The standard gates, plus on the local stack:
1. With `GMR` mode `manual`, approving a manifest enqueues nothing (check queue depth in Azurite).
2. Record a GMR reference. Its declaration is `Filed`.
3. Switch to `automatic`, file the ELO, and the envelope appears.
4. An ELO without an accepted ENS returns 409.

## Sequencing

After plans [04](04-receiver-registration.md), [05](05-item-classification-value.md) and
[07](07-box-allocation-ferry.md). [Plan 09](09-declaration-staleness.md) follows immediately.

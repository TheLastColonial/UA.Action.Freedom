# Goods Movement Flow: Box Validation to GMR/ELO

Sequence diagram showing how a box progresses from validation through manifest approval to GMR submission and French logistics envelope (ELO) generation.

See `goods-movements.puml` in this directory for the PlantUML diagram.

## Flow Summary

### 1. Box Validation
- Loader validates a box: `POST /boxes/{id}/validate`
- `ValidatedAt` and `ValidatedByPersonId` are set atomically
- Box is now **frozen** — cannot add/remove items, change receiver, or invalidate

### 2. Manifest Creation & Cargo Assembly
- Dispatcher creates manifest for a convoy/vehicle pair: `POST /convoys/{convoyId}/vehicles/{vin}/manifest`
- Loader adds validated boxes to manifest: `PUT /manifests/{id}/boxes/{boxId}`
- Manifest progresses through states: **Proposed** → ... → Confirmed (frozen)

### 3. ENS Declaration (Outside Freedom)
- Ground Officer files Entry Summary Declaration in EU Customs Trader Portal (ICS2)
- Dispatcher records the MRN in Freedom: `PUT /manifests/{id}/ens`
- MRN is stored in blob storage (`ens/{manifestId}/declaration-*.json`)
- **Required before approval** — manifest cannot freeze without an ENS MRN

### 4. The Approval Fork
Admin approves manifest: `POST /manifests/{id}/approve`

**Pre-Freeze Check:**
- Verify ENS MRN is recorded
- If missing → 409 EnsNotFiled (manifest stays editable)
- If present → proceed to freeze and handoffs

**Freeze & Enqueue (Atomic):**
- Set `Manifest.Status = Confirmed`
- Set `Manifest.GmrSubmittedAt = NOW()`
- Enqueue three independent jobs:
  1. GMR submission to HMRC
  2. ELO envelope creation for French Customs
  3. Manifest document generation (redacted, no addresses)

### 5. Three Parallel Handoffs

#### GMR Submission (Customs Worker)
- Dequeues from `customs-work`
- Calls HMRC Goods Vehicle Movement System API
- Stores response in `dbo.GmrDocument` (blob storage)
- Manifest now has HMRC's submission ID and Local Reference Number

#### ELO Envelope Creation (Customs Worker)
- Dequeues from `elo-envelopes`
- Reads ENS MRN from blob storage
- Calls French Customs EDI API with crossing profile (Humanitarian Aid to Ukraine)
- Receives `numeroDossier` and PDF barcode (base64)
- Stores envelope in `dbo.EloEnvelope` and blob storage
- **Not idempotent** — retry with same ENS MRN creates second envelope

#### Manifest Document Generation (Manifest Worker)
- Dequeues from `manifest-documents`
- Reads manifest, boxes, crew, insurance from database
- **Cannot read receiver detail** — no access to `ISensitiveDbConnectionFactory`
- Renders plain-text travelling document (deterministic, testable)
- Uploads to blob storage (`manifests/{manifestId}/manifest.txt`)
- No delivery address leaves the system

### 6. Ready for Departure
Manifest transitions through remaining states:
- **Confirmed** (frozen) — cannot edit items, crew, insurance
- **Ready** — all boxes prepared, loaded
- **InTransit** — convoy has departed
- **Delivered/Lost/Returned** — journey complete, vehicles handed over

Status transitions are **reports of what happened**, not edits of the frozen state.

## Constraints & Invariants

| Invariant | Enforcement |
|-----------|------------|
| ENS MRN required before approval | Check in `ApproveManifestHandler`, throw 409 before freeze |
| Manifest frozen after approval | `GmrSubmittedAt IS NOT NULL` blocks all edits except status transitions |
| Box locked after validation | `ValidatedAt IS NOT NULL` blocks all edits to box |
| Receiver address not in traveling document | Manifest worker has no `ISensitiveDbConnectionFactory` injected |
| Delivery address never logged | Redaction in `ReceiverReadModel` (no address field) |
| Delivery address access audited | Every resolve writes audit row in same transaction |
| One envelope per ENS | ELO API is not idempotent; retry creates second envelope |
| GMR & document idempotent | Can replay job (idempotency key or conditional insert) |

## Gotchas

- **ENS MRN is write-once**: Stored in blob via conditional create (`IfNoneMatch = ETag.All`). Invalidate & refile via `SupersedeAsync` (copies to `ens/{manifestId}/superseded-*.json` before delete).
- **ELO envelope carries only declaration identifier**: No independent data, pairs crossing against ICS2 MRN. Address stays on filing sheet shown to Ground Officer only.
- **Manifest document is plain text on purpose**: Deterministic and testable; a PDF wrapper can be applied later without changing what is written.
- **Three handoffs, three failure modes**: GMR stuck on queue = manifest frozen with no HMRC confirmation (visible, retryable). ELO stuck = no barcode for border handover. Document stuck = no travelling paperwork. Each is queue-watched separately.

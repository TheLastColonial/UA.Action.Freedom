# 2. The French logistics envelope hangs off the manifest and is requested by approving it

Date: 2026-09-27

## Status

Accepted.

## Context

From April 2026 France requires an **ELO** (*Enveloppe Logistique Obligatoire*) per transport unit at
the Smart Border on a UK → France RoRo crossing. No envelope, no crossing — so for Ukrainian Action
this is not paperwork that can be caught up later.

`src/EDI.ELO` already held a generated client for `API_BREXIT_ELO` v1.2.0 and nothing consumed it.
`docs/gotchas-and-open-questions.md` §8 named the gap precisely: *"no worker submits an ELO envelope,
no stub exists in `iac/local`, and there is no mapping from Freedom's own manifest/declaration data to
the ELO request shape."*

The first thing that had to be settled was what an envelope actually is, because the obvious reading
is wrong. `ENV_CRE01` is:

```json
{ "informationsAppairage": { "sensTraversee": "IMPORT", "typeCamion": "PLEIN",
    "estTIRATA": true, "possedeContratTransport": false, ... },
  "identifiantsDeclaration": [ "25FR17551780961AT5" ] }
```

Crossing flags, and a list of **declaration identifiers issued by other systems**. No goods
description, no weights, no consignor, no consignee, no addresses, not even a registration. It is an
index, not a declaration.

## Decision

### The envelope is per manifest, not per convoy

An ELO covers one transport unit. A transport unit is a lorry. A lorry on a convoy is a
`ConvoyVehicle`, and the paperwork for a `ConvoyVehicle` is its `Manifest` — which already holds the
GMR. So the envelope sits beside `Manifest.GmrSubmittedAt` and is addressed as
`/manifests/{id}/elo`.

### Approval requests it, exactly as it requests the GMR

`docs/process.puml` has always forked `Generate GMR` and `Generate ELO` in parallel off **Manifest
Approved**. `ApproveManifestHandler` gains a third `HandOff("elo", …)` beside the existing two. The
freeze still happens first, and a failed hand-off still counts, logs and rethrows — a manifest frozen
with paperwork that will never be produced is visible and retryable, which is the trade §5.2 already
accepted for the GMR.

There is deliberately **no** `POST /manifests/{id}/elo`. Requesting an envelope is a consequence of
approval, which is Administrator-only; a second route to it would be a second way to get two
envelopes for one lorry.

### Its own queue, inside the existing Customs Worker

`elo-envelopes` and `elo-envelopes-poison`, drained by a third loop in `CustomsWorkerService`.

Not a fourth service, because a container costs compute in a design constrained to permanent free
allowances (`docs/recommendations.md` §2), and because the Customs Worker is already the thing that
talks to border authorities. Not a message type on `customs-work`, because a refusal from French
customs and a refusal from HMRC need different people and different fixes, and one dead-letter pile
nobody can triage is worse than two.

### Nothing is dead-lettered after French customs has accepted an envelope

This is the one rule the GMR side does not need, and it is the reason the dispositions differ.

Creating an envelope is **not idempotent**, and the `numeroDossier` that identifies it exists only in
the creation response. Discarding that message after a successful create would leave an envelope at
French customs that nobody in Freedom can name, modify or present at a border. So:

| Situation | Disposition |
| --- | --- |
| Unreadable message, or no manifest reference | Dead-letter, without calling customs |
| Loaded lorry naming no declaration (ENV_CTR_RG08) | Dead-letter, without calling customs — the answer is already known |
| Customs answered 4xx | Dead-letter; retrying gives the same answer |
| Created, stored, done | Complete |
| Created but the barcode was absent or not base64 | **Store the reference without it, and complete.** A barcode can be fetched again; a forgotten file number cannot |
| Anything else, including a store that failed after a successful create | Leave it; the visibility timeout retries it |

The last row is a considered trade: a retry after a successful create makes a second envelope at
customs, which is unwanted but visible and recoverable, where completing the message loses the first
one for good.

### The worker still has no database

It writes the envelope and its barcode to the `elo` container, and the API reads them back through
`IEloEnvelopeStore`. The worker could have been given a connection string and a table; it was not,
for the same reason the Manifest Worker has none — a worker that cannot read anything cannot read a
Ukrainian delivery address. `GET /manifests/{id}/elo` and `/elo/document` exist so that choice costs
nothing in visibility.

### The crossing profile is domain, not configuration

`EloCrossingProfile.HumanitarianAidToUkraine` — `IMPORT`, `PLEIN`, TIR/ATA, no transport contract —
lives in the domain with its citation, not in `appsettings`. Every flag is a ruling that changes what
French customs demands in the same envelope, and `estTIRATA` in particular is load-bearing: under
ENV_CTR_RG08 a loaded lorry *outside* TIR/ATA must present an ENS **and** a customs-clearance
formality, where under TIR/ATA the ENS alone suffices. That is not something an environment variable
should be able to flip.

## Consequences

**The path is complete and the declaration is not.** An envelope references formalities issued by
ICS2 and DELTA-T, and Freedom integrates with neither. `Elo:PlaceholderDeclarationIdentifier` carries
a stand-in that the local stub accepts and real French customs would refuse with `FONC-ERR-004`. The
whole durable path — enqueue, authenticate, submit, decode, store, serve — is real and tested; the
one thing missing is a real ENS. Integrating ICS2 closes it, and at that point the identifiers become
per-manifest data and the setting goes away.

**A codegen bug had to be fixed first.** The published spec declares the barcode as
`pdf: { type: string, enum: [formatbytebase64] }` while its own examples send base64, so NSwag
generated a one-member enum with a converter that throws on every successful response. Corrected in
`build/nswag/elo.preprocess.json`, which also carved `Authorization` out of the transport-header
strip — meaning `-Api elo` no longer requires `-Raw`.

**Status stops at `FERMEE`.** Pairing, embarkation and disembarkation are pushed as `ENV_NOT01`
notifications to a callback URL we decline to register (`recommendations.md` §4.1), and the pull
alternative — polling `POST /enveloppe/recuperer` — is not built. A dispatcher can see that an
envelope exists and print its barcode; they cannot yet see that the lorry boarded.

**Two authorities now share one worker, one dead-letter counter and one dashboard.**
`freedom_gmr_dead_letters_total` is told apart by `reason`; submission duration is not, because
different authorities with different latencies would blur into one histogram. If a third authority
ever appears, that is the seam to reconsider.

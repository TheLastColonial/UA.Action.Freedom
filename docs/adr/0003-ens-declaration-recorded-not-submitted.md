# 3. The ICS2 declaration is recorded, not submitted, and approval will not proceed without one

Date: 2026-09-27

## Status

Accepted. **Amended on 2026-10-02.** The decision not to submit an ENS stands. Who files it has changed: the
**Dispatcher** files it, and a Ground Officer may answer questions about destinations
([decision X1](../domain/decisions.md#x1)), where this ADR had a Ground Officer file it. The ENS is also now one
of a vehicle's [declarations](0005-declarations-are-per-vehicle-with-derived-staleness.md). See
[Amendments](#amendments).

## Context

`docs/adr/0002-elo-envelope-on-manifest-approval.md` shipped a complete French logistics envelope and
ended on one gap:

> **The path is complete and the declaration is not.** … `Elo:PlaceholderDeclarationIdentifier`
> carries a stand-in that the local stub accepts and real French customs would refuse with
> `FONC-ERR-004`. … **Integrating ICS2 closes it.**

`docs/gotchas-and-open-questions.md` §8 called it *"the one thing between the ELO integration and a
usable border document."* This closes it.

The dependency is hard rather than advisory. The ELO cannot be created without the ICS2 ENS MRN; the
sequence is ENS → MRN → ELO → barcode → check-in, and there is no catching up at the border. Because
`EloCrossingProfile.HumanitarianAidToUkraine` declares TIR/ATA, `ENV_CTR_RG08` reduces the envelope's
requirement to a **single ENS** — so exactly one MRN per manifest makes the whole envelope valid.

Both regimes are already live. ENS for accompanied road freight became mandatory on **1 September
2025**; the ELO became mandatory for all Channel crossings on **20 April 2026**. A convoy cannot
lawfully cross today without both, and before this change Freedom could produce neither for real.

Two facts about the obligation shaped everything below:

- **Accompanied road freight has exactly one filer, and it is Ukrainian Action.** The carrier is the
  operator of the active means of transport, the charity's own volunteers drive, and for an
  accompanied movement only one party may lodge the ENS. There is no forwarder to defer to.
- **The Shared Trader Interface is not an HTTP API.** It runs on EU eDelivery **AS4** (ebMS3/AS4,
  SOAP 1.2 with attachments), with a TLS certificate, a separate eIDAS sealing certificate from a
  LOTL-listed CA registered in UUM&DS, and a mandatory self-conformance run in the CONF environment
  before production. `src/EDI.ELO`, `src/HMRC.GVMS` and `src/HMRC.PushPullNotifications` are all
  NSwag-generated clients from a committed OpenAPI spec. That pattern does not transfer, and pretending
  otherwise would have produced a client that cannot connect.

## Decision

### Freedom records the MRN; it does not submit the declaration

There is no `src/ICS2.*` project, no `build/nswag` entry and no queue. A Ground Officer files in the
EU Customs Trader Portal — `https://customs.ec.europa.eu/gtp/`, conformance at
`https://conformance.customs.ec.europa.eu/euctp` — and a Dispatcher records the MRN with
`PUT /manifests/{id}/ens`.

Running an AS4 access point is not a smaller version of what the other integrations do; it is the
opposite of the shape this system is built in. It needs a permanently reachable **inbound** endpoint,
which `docs/recommendations.md` §4.1 refuses for HMRC on purpose — *"Push requires a permanently
reachable public endpoint, a shared secret to rotate, callback authentication to get right, and replay
protection"* — and an always-on container, which the permanent-free-allowance hosting model (§2) has no
room for. On top of that sit certificate procurement, UUM&DS registration and a conformance run whose
lead time is measured in weeks.

So the port is the seam. `IEnsDeclarationStore` is where a commercial IT Service Provider's adapter
drops in later: an ITSP operates the access point, exposes JSON or XML over HTTP, and returns the MRN
and status, while Ukrainian Action remains the declarant. That is a procurement decision, not a coding
one, and nothing above the port changes when it is made.

### Approval refuses before it freezes

This is the one place in the manifest lifecycle where a check happens **before**
`ConfirmAndFreezeAsync`, and the exception is deliberate. Every other refusal in
`ApproveManifestHandler` is about the manifest's own state, and freeze-then-enqueue is what stops an
editable manifest whose GMR is already on its way (§5.2). But a manifest frozen with no declaration is
frozen for ever against an envelope French customs will never issue — which is precisely what the
placeholder setting used to produce, only later and as a `FONC-ERR-004` in a worker log.

So `TransitionManifestOutcome.EnsNotFiled` → 409, nothing written, and the manifest still approvable
once the MRN arrives. The 409 names the route to fix it.

### The MRN is a blob, and it is write-once

`ens/{manifestId}.json`, beside the envelope that names it, read and written through
`IEnsDeclarationStore` — the same shape as `IEloEnvelopeStore`, and for the same reason: this is border
paperwork *about* a manifest rather than part of what a manifest is.

A blob has no `WHERE` clause, so the write-once discipline `Manifest.GmrSubmittedAt` gets from a
conditional `UPDATE` is bought back at the storage layer. `SaveAsync` uploads with
`IfNoneMatch = ETag.All` and reports the conflict rather than throwing, so the storage service settles
a race between two dispatchers instead of a read-then-write in C#.
`tests/UA.Action.Freedom.Tests.Integration/Manifests/EnsDeclarationStoreTests.cs` proves that against a
real account, because a substituted blob client would only prove we pass a condition, not that the
service honours it.

### Correcting a declaration is an explicit supersede, not an overwrite

Several ENS fields are **non-amendable** — mode of transport, declarant, customs office of first entry,
the carrier identifier, transport document references, the goods item number. Correcting one means
invalidating the declaration in ICS2 and filing a new one, so that is a path Freedom has to support
rather than an exception. `DELETE /manifests/{id}/ens` copies the current declaration to
`ens/{manifestId}/superseded-{timestamp}.json` **before** deleting it — the same ordering, and the same
reason, as `AzureEloWorkQueue.DeadLetterAsync`. A customs query months later is about the MRN that was
filed at the time, which may well be one that was withdrawn.

It is refused once the manifest is frozen: the envelope already names that MRN, and withdrawing it
would leave French customs pairing a crossing against a formality Freedom no longer believes in.

### The filing sheet is deliberately incomplete, and says so

`GET /manifests/{id}/ens/filing-sheet` composes everything an ENS asks for that Freedom can know:
declarant and carrier EORI, consignor, office of first entry, mode of transport, active and passive
means of transport, countries of routing, goods items with commodity codes, package counts, gross mass,
and the consignee at **organisation and region only**.

The consignee's address is not on it and never will be. It lives in the `sensitive` schema behind
`receivers:detail`, the sheet is composed on a connection that is `DENY SELECT`'d there, and a sheet
listing precise Ukrainian delivery addresses is a targeting document
(`docs/domain/key-concepts.md` § Data Sensitivity). The filer already holds that address — they are the
only role that may read it — so `ConsigneeAddressWithheld` is `true` and `ConsigneeAddressSource` names
`GET /receivers/{ref}/detail`, because a filer who simply saw no address might conclude there is none.

The sheet also reports its own gaps in `Missing`: an item with no commodity code, named; a route stop
with no ISO country code, named; a ferry crossing with no vessel IMO; boxes nobody has validated, so
the gross mass is provisional. A declaration refused at the border costs a convoy. A gap reported here
costs a phone call.

### Recording the MRN is a Dispatcher's act, not a Ground Officer's

`manifests:declare` is Administrator and Dispatcher. The person who *files* the declaration is a Ground
Officer, because filing needs the consignee address — and GroundOfficer is excluded from every manifest
policy, with the isolation running both ways (`docs/local-authentication.md`). Widening it for this one
route would put the narrowest role in the system inside the manifest slice.

So it is a two-person hand-off, the same shape as a Dispatcher building a manifest an Administrator
signs off: the Ground Officer takes the filing sheet, fetches the address under their own policy, files,
and passes back the MRN.

### The crossing moved onto the convoy

`dbo.Convoy` gains `CrossingMode` and `VesselImo`, because the ENS mode-of-transport code is a property
of the crossing and not of the cargo: a ferry sailing is maritime (1) and a shuttle crossing is road
(3), even though a lorry drives onto one and is carried by the other. Rail (2) is not accepted at the
Brexit Smart Border. A ferry names the vessel as its active means of transport and so needs an IMO from
the official list for the route; the shuttle names the lorry's own plate.

This is the first half of what `docs/gotchas-and-open-questions.md` §9 predicted for
`Customs:RouteId` — *"if convoys ever cross by more than one route it becomes a property of the
convoy"*. `Ens:OfficeOfFirstEntry` is still configuration, and moves next to `CrossingMode` when a
second crossing point appears.

Neither is write-once: the crossing may change while the convoy is planned. It stops being changeable
once the ENS is filed, because both fields are non-amendable in ICS2 — but that is enforced by the
declaration existing, not by a stamp.

## Consequences

**`Elo:PlaceholderDeclarationIdentifier` is gone**, along with the API's whole `EloOptions` class, the
compose variable and the `.env.example` block that called itself *"NOT PRODUCTION-READY"*. The
envelope now carries the MRN the crossing was actually accepted under, and §8's ICS2 row is closed.
`AzureManifestWorkQueue` keeps its `ENV_CTR_RG08` pre-flight guard, but it is now a guard against a
caller bypassing `ApproveManifestHandler` rather than the last stop before a stand-in.

**The ENS MRN does not reach HMRC, and that is a finding rather than an omission.** GVMS has the right
field — `sAndSMasterRefNum`, *"the Movement Reference Number for a Safety & Security declaration …
applies to both ENS and EXS"* — but it hangs off a declaration container, and every container requires
a primary identifier Freedom does not hold: `customsDeclarations[].customsDeclarationId` is a CDS DUCR
for an outbound movement, `tirDeclarations[].tirCarnetId` a TIR carnet number,
`ataDeclarations[].ataCarnetId` an ATA one. Putting the MRN in `customsDeclarationId` would file an
ICS2 reference as a CDS one. The spec also scopes ICS2 MRNs to the `GB_TO_NI` direction, while these
movements are `UK_OUTBOUND`. `GmrSubmissionRequest` has a property-set test pinning its absence with
that reasoning attached, so adding it later is a deliberate act.

**The filing type is not asserted anywhere yet.** Secondary sources disagree between F50 and F40 for a
complete road ENS, and the authority is the ICS2 Functional Specifications in the CIRCABC group
`18fb5859-3970-4ac5-b30b-6604977a15a7`. The filing sheet carries the mode-of-transport code, which is
unambiguous, and leaves the dataset code to the portal, which offers it as a choice.

**Ukrainian Action is UK-established, and an ENS declarant's EORI must be issued by an EU member
state.** A GB EORI is not accepted. The Commission's own FAQ does not say what a non-EU carrier must
do, and this is the open administrative question in `docs/schemas/ics2/onboarding.md`. It may turn out
that an ITSP or an EU-established representative is mandatory rather than optional — in which case the
port built here is the thing that absorbs it.

**Approving a manifest is now gated on work done outside Freedom.** That is a real operational cost:
a dispatcher who has not been given an MRN cannot approve, and nothing in Freedom can produce one for
them. It is the honest cost of the regime, and it is visible — a 409 naming the route — rather than a
convoy turned away at Calais.

**The filing sheet is a new read that composes across three slices** (manifest, convoy, boxes) and is
not a submission. If an ITSP adapter arrives, it is the natural input to it: the payload it would send
is this sheet plus the consignee address, which is the one field that would then have to cross the
`sensitive` boundary — and that decision deserves its own ADR rather than being smuggled in as an
implementation detail.

## Amendments

Added 2026-10-02. The original text above is kept as it was decided.

### Who files the ENS

The section "Recording the MRN is a Dispatcher's act, not a Ground Officer's" described a **two-person
hand-off**: the Ground Officer takes the filing sheet, reads the address under their own policy, files, and passes
back the MRN. **That is replaced.** The Dispatcher files the ENS in the EU Customs Trader Portal and records the
MRN, and a Ground Officer may work alongside to answer questions about destinations
([decision X1](../domain/decisions.md#x1)).

What this ADR protects is unchanged **inside Freedom**: the filing sheet still withholds the consignee's address,
`receivers:detail` is still Ground Officer alone, and `GroundOfficer` is still excluded from every manifest
policy. What changes is a **human control outside the system**. Previously the person typing an address into the
portal was the only role allowed to read it. Now the Dispatcher types, or watches, while a Ground Officer supplies
it. The system cannot enforce that, and it should be treated as an accepted process risk, not a technical one
([Q-ens-address-handling](../domain/decisions.md#q-ens-address-handling)).

The statement "The filer already holds that address" is no longer true of the filer. `ConsigneeAddressWithheld`
and `ConsigneeAddressSource` remain, and now tell the Dispatcher **to ask the Ground Officer** and not to look it
up.

### Declaration shape and submission mode

| Part of this ADR | Now |
| --- | --- |
| `ens/{manifestId}.json`, keyed on the manifest | Held as one of the vehicle's declarations ([ADR 0005](0005-declarations-are-per-vehicle-with-derived-staleness.md)). The blob store, the `IfNoneMatch` write-once rule and `IEnsDeclarationStore` as the ITSP seam all stand. |
| "Correcting a declaration is an explicit supersede" | **Stands, and is generalised.** Invalidate-and-refile is the standard correction when the load changes, and the superseded MRN is kept. |
| "Approval refuses before it freezes" with `EnsNotFiled` | **Stands**, adjusted: a vehicle needs a current ENS before it can depart ([ADR 0008](0008-readiness-is-computed-and-blocking-rules-are-not-overridden.md)), and approval still refuses when none is recorded where the ELO would otherwise be requested. |
| The ENS is recorded, never submitted | **Stands.** Manual filing is now the default for every authority ([ADR 0006](0006-filing-is-manual-by-default.md)), so this ADR's reasoning for the ENS is the general rule. |

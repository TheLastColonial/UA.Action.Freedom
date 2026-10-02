# Key Concepts

The shared vocabulary for Freedom. Terms defined here should be the terms used in code, in the C4 diagrams and in
conversation with Ukrainian Action.

Related: [System Context](../c4/1-system-context.puml) · [Containers](../c4/2-containers.puml) ·
[Manifest creation process](../process.puml) · [Manifest status](../manifest-status.puml) ·
[Architecture recommendations](../recommendations.md) · [Sequence diagrams](../sequences/README.md) · [Process diagrams](../process/README.md) ·
[State diagrams](../states/README.md) · [Use cases](../use-cases/README.md) · [Domain model](../model/domain-model.puml) · [Convoy timeline](../timeline/convoy-timeline.puml)

---

## Operating Rhythm

Freedom is not a steady-state system. **Convoys run roughly once a month.** The month looks like a long quiet
period followed by a burst of a few days spanning manifest building, box preparation, loading and departure.

This is a domain fact with real consequences, so it is recorded here rather than buried in an architecture
document: the system is sized to cost nothing while idle and to scale up for the convoy window. Cold starts
between convoys are an accepted trade. See [recommendations §2.2](../recommendations.md#22-the-container-apps-budget-in-hours).

The **convoy window** — the days either side of departure when the system is actually in use — is therefore a
first-class operational concept, not just a date range.

For scale: about 500 vehicles over four years, one convoy a month, and about 25 users
([O23](decisions.md#o23)).

---

## Roles

*Diagrams: [People and access (use cases)](../use-cases/people-and-access.puml), [Convoy operations (use cases)](../use-cases/convoy-operations.puml), [Boxes and donations (use cases)](../use-cases/boxes-and-donations.puml), [Declarations and Receivers (use cases)](../use-cases/declarations-and-receivers.puml).*

Each role below maps to one application role in the identity provider. They are deliberately narrow: least
privilege is easier to keep when the roles already describe distinct jobs.

### Administrator

Manages access for individuals and the roles they may hold on Freedom. Approves new volunteers and revokes
access when they leave.

### Dispatcher

Responsible for creating a [Manifest](#manifest) and acting as the communication point for convoy team leaders.
Books ferry crossings and triggers the [GMR](#gmr--goods-movement-reference) and [ELO](#elo--obligatory-logistics-envelope)
paperwork. Also the booking of hotel accommodation for the trip. They will also coordinate with third parties to
arrange servicing — though recording the result of a vehicle's inspection is the [Mechanic](#mechanic)'s job, not
theirs.
Also to find insurance for the [Driver](#driver) and [Vehicle](#vehicle).

### Loader

Responsible for being on site, verifying the contents of donations and loading the vehicles in the next convoy.
Contents must be verified to weigh them for border checks and to assure the contents, as this is a trust
boundary between the donor and Ukrainian Action.

Also the only role that may place a [Box](#box) in a [Bay](#bay) — narrower even than validating a box,
because it is the physical, on-site act of shelving one so it can be found again for loading.

A Loader **sees only the locations they manage** ([O14](decisions.md#o14)).

### Purchaser

Responsible for the sourcing of vehicles, equipment and other sundries consumed in a convoy.
Including transportation of the purchase to the logistical hubs.

### Mechanic

Responsible for the mechanical readiness of the donated [Vehicles](#vehicle). A Mechanic records vehicles as they
come in, keeps each one's details current — mileage, condition notes, kerb weight, cargo capacity — and carries out
the servicing inspection: moving a vehicle through **Pending → Inspecting → Passed** or **Failed**, and writing down
any defects found in the inspection notes (see [Inspection Status](#inspection-status)).

That result matters beyond the workshop. A vehicle is itself part of the aid and is handed over in Ukraine, so a
vehicle that fails on the road is a failed delivery: **only a vehicle the Mechanic has marked Passed may be assigned
to a [Convoy](#convoy)**, and the API refuses any other.

- An Administrator may also record an inspection. A Purchaser, who can edit a vehicle's details, cannot — sourcing a
  vehicle and vouching that it is roadworthy are different acts (the `vehicles:service` policy).
- The inspection is recorded through its own route, `PUT /vehicles/{vin}/inspection`; an ordinary vehicle edit
  cannot set, clear or forge it.
- The role is deliberately narrow, like the others: a Mechanic has no access to convoys, manifests, boxes,
  volunteers or receivers.

### Ground Officer

Responsible for communicating with the local authorities in Ukraine:

- Ensuring requests for aid are collected and documented.
- Ensuring aid that is delivered is accepted.
- **Segregating the delivery logistics from the sensitive details of a delivery** — see [Data Sensitivity](#data-sensitivity).

The Ground Officer is the only role that sees full [Receiver](#receiver) detail.

### Driver

A volunteer who drives a vehicle on a convoy. Drivers are notified of their allocation and receive
their manifest, but do not administer the system. A driver may be *committed* to a convoy or merely available.

### Volunteer erasure

A volunteer who leaves can ask to be erased, and UK data protection gives them that right. Freedom **deletes their
personal data** — name, date of birth, phone, driving status — rather than hiding it. The records they were part of
(past convoy crews, who validated or shelved a box) keep an anonymous identity in their place and read
**"Former volunteer"**; nothing links that identity back to the person. Someone no record names is removed
outright.

A volunteer's **login link** (`PersonDetail.IdentitySubject`, set by an Administrator) is part of the personal data,
so erasure removes it and the login reads as not linked. A login that is not linked cannot do anything that records
who did it: such writes are refused with `403 login-not-linked`.

Erasure is **refused while the volunteer is still needed**: on the crew of a convoy that has not arrived, or on the
team of a manifest still under way. Take them off it first. Only the Administrator erases, and the operator UI asks
for confirmation, since it cannot be undone.

### Donor _(external)_

A person or organisation donating a vehicle, goods or funds. Interacts with the public website, not with Freedom
directly.

### Border Guard _(external)_

Represents a country's border authority. Verifies a load in transit. Has no account and no standing access — see
[Manifest Verification](#manifest-verification-proposed).

---

## Core Concepts

### Convoy

*Diagrams: [Convoy lifecycle (state)](../states/convoy-lifecycle.puml).*

A collection of [Vehicles](#vehicle) travelling together to Ukraine, with a departure timestamp, an expected
arrival timestamp and a [Route](#route). The convoy is the unit that is planned; the [Manifest](#manifest) is the
unit that is executed per vehicle.

The fact that joins them — *this vehicle is travelling with this convoy* — is one row, the
[Truck List](#truck-list) entry. The crew, the insurance and the manifest all hang off it, so none of them can
describe a truck that is not on the list.

#### Readiness

*Flows: [07 Departure](../sequences/07-departure.puml) ([process](../process/07-departure.puml)).*

A convoy is **ready** when it has a route, has vehicles still travelling with it, and every blocking requirement is
met for the convoy and for each of those vehicles. The Dispatcher sees what is still outstanding on the convoy's
overview. Which requirements **block departure** and which only **warn** is set out in
[Convoy operations](convoy-operations.md#readiness) ([P4](decisions.md#p4)).

A [withdrawn](#withdrawal) vehicle is skipped entirely rather than reported as unready: it has no crew to find and no
insurance to renew.

#### Arrival

A convoy **arrives** when the Dispatcher marks it so (`POST /convoys/{id}/arrive`), which is allowed only once its
truck list is published and **every vehicle still travelling with it has a finished manifest** — Delivered, Lost or
Returned. Until then the request is refused and names the vehicles still travelling. A
[withdrawn](#withdrawal) vehicle is not waited for: it broke down and left, and holding the convoy open for it would
mean the convoy could never arrive. Arrival, in one step:

- **Delivered and Lost vehicles are handed over.** A vehicle is itself part of the aid and stays in Ukraine, so it
  is stamped `HandedOverAt` and is never offered for a convoy again.
- **Everything else is simply free again.** Nothing is *released*: a vehicle that was not handed over may join the
  next convoy because this one has arrived, not because a pointer was cleared — which is why the truck list survives
  as the record of who went. (Before the truck list became a table, arrival nulled `Vehicle.ConvoyId` to release a
  vehicle, and an arrived convoy lost its own list.)
- **The journey becomes history.** An arrived convoy takes no further crew or insurance changes; its crew list is
  the record of who went.

### Vehicle

A truck or car that has been donated, and which is itself part of the aid — vehicles are handed over in Ukraine,
not driven back. Identified by VIN and licence plate, and carrying the detail a border check needs: kerb weight,
fuel type, transmission, year, and condition notes — plus descriptive detail (brand, model, colour), mileage,
and whether it has been serviced. A vehicle also optionally records who purchased it and when (`Purchaser`/
`PurchaseDate`) — see [Purchaser](#roles) below for the role.

A vehicle may also carry an optional **cargo capacity**: a maximum cargo weight and the width, depth and height of
its cargo space. Nothing back-fills this — it starts unset and is measured whenever someone gets round to it. It
is distinct from the kerb weight above (the vehicle's own weight) and exists to help a dispatcher judge, before a
convoy leaves, whether the [Boxes](#box) assigned to a [Manifest](#manifest) are too heavy or too large for the
vehicle carrying them — see the note under Manifest.

#### Inspection Status

Every vehicle carries an **inspection status**, recorded by a [Mechanic](#mechanic), that tracks its mechanical
inspection:

- **Pending**: The vehicle has not been inspected yet — the state every new vehicle starts in.
- **Inspecting**: The vehicle is currently being inspected by a Mechanic.
- **Passed**: The vehicle has completed inspection and is ready for convoy. Only vehicles in this state are eligible
  for assignment to a convoy.
- **Failed**: The vehicle has completed inspection but was found to have issues that prevent it from being used in a
  convoy.

The inspection status is separate from the `Servicing` flag: `Servicing=true` means a vehicle is in for servicing,
while the inspection status tracks how far through that process it has progressed. The Mechanic may also record
**inspection notes** (up to 2,000 characters) documenting any defects, damage, or issues found during inspection.

> **Naming:** the domain type was renamed from `Veichle` to `Vehicle`. The rename is complete across the
> solution.

### Vehicle Crew

The people travelling in a [Vehicle](#vehicle) on one [Convoy](#convoy), decided while it is planned. There are no
journey legs: a crew seat is one person on one vehicle on one convoy ([P12](decisions.md#p12)). Each crew member is
either:

- a **Driver** — a volunteer registered to drive; or
- a **Passenger** — any volunteer.

**This is the only crew record in the system.** The [Manifest](#manifest) reads it; it does not keep its own.

**A person takes one seat per convoy**: they cannot be in two vehicles on the same convoy. A vehicle needs **at least
one driver** to depart, and **two drivers** are advised for sustained driving and border compliance; passengers do not
count ([P9](decisions.md#p9)). Crewing is the [Dispatcher](#dispatcher)'s alone.

There is no primary/secondary distinction. Two drivers are two drivers, and readiness counts them.

The crew can still change after the truck list is published — a driver falls ill — but **adding a driver**
needs the vehicle's [insurance](#vehicle-insurance) updated before it departs ([O7](decisions.md#o7)); removing one
does not affect it. Once the convoy has [arrived](#arrival), or the vehicle has [withdrawn](#withdrawal), the crew is history
and cannot change.

### Vehicle Insurance

*Flows: [04 Convoy planning](../sequences/04-convoy-planning.puml) ([process](../process/04-convoy-planning.puml)).*

Bought by the Dispatcher for each vehicle on a convoy, and it **covers the drivers named on it**. It is for the
vehicle to travel on the road; cargo is not insured ([O5](decisions.md#o5)). Recorded per vehicle
per convoy: insurer, policy number, cover start and end, optional cost, and who recorded it (taken from their login,
never typed in). Dispatcher and Administrator may record it (`PUT /convoys/{id}/vehicles/{vin}/insurance`).

- **Removing a crew member does not void it.** **Adding a driver** needs it updated with the insurer, which the
  Dispatcher records ([O7](decisions.md#o7)).
- **A manifest cannot depart without it** — recorded, in cover on the day, and covering every driver — and is
  refused with the reason.
- Taking the vehicle off the convoy, or cancelling the convoy, removes it.

The crew is a property of the vehicle within the convoy. A manifest used to carry its own primary/secondary
*driver teams* alongside it, set through a different endpoint and connected to this by nothing at all — so a printed
manifest could name a crew the insurance had never heard of, while the insurance is what actually gates departure.
There is one record now.

### Route

The ordered list of [Addresses](#address) a convoy will pass through, from UK departure to Ukrainian delivery.

### Address

A location in the real world. Note that not all addresses are equally sensitive: a UK depot is routine, a
Ukrainian delivery address is not. See [Data Sensitivity](#data-sensitivity).

### Item

A single donated thing, with a description and open-ended properties. Items are not tracked individually in
transit — they are tracked as the contents of a [Box](#box).

### Box

*Diagrams: [Box lifecycle (state)](../states/box-lifecycle.puml).*

*Flows: [02 Donation and box intake](../sequences/02-donation-and-box-intake.puml) ([process](../process/02-donation-and-box-intake.puml)), [03 Box replacement](../sequences/03-box-replacement.puml) ([process](../process/03-box-replacement.puml)).*

A packed container of [Items](#item) with a confirmed weight, a current [Location](#location), and a target
[Receiver](#receiver). A box is **validated** when a [Loader](#loader) has confirmed its contents and weight;
the system records who validated it and when.

A box's `LocationId` records which distribution hub it has arrived at — set independently of, and
before, which [Bay](#bay) it has been shelved in. A box can be checked in at a location before a
Loader gets round to placing it in a specific bay.

Validation is the trust boundary between the donor and Ukrainian Action, and the weight it produces is what the
border check relies on. Both facts make the validation record an audit artefact, not just a status flag.

A box may also carry optional **dimensions** (width, depth, height), set at the same moment as the confirmed
weight — a Loader is physically looking at the box then. Like the weight, dimensions start unset and are only
ever written by validation.

A box carries a **QR label**: an opaque, non-enumerable token that a scan resolves back to the box's record
(`GET /boxes/scan/{token}`). A box can be re-labelled — issuing a new code revokes the previous one, so a label
lost in transit is replaced and the old one stops working. The label is printed by the system and contains a box
number, the token and the charity name — and deliberately nothing else. It travels with the box and may be
inspected at a border, so it names no [Receiver](#receiver), region or [Address](#address); see
[Data Sensitivity](#data-sensitivity). Issuing or reprinting a label is allowed at any point in a box's life,
including after validation — a label is not box contents, so the freeze that protects the confirmed weight does
not apply to it.

### Location

A distribution hub — a garage or warehouse — where [Boxes](#box) are stored between arriving and being
loaded for a convoy. Subdivided into [Bays](#bay). A location is a first-class entity (name plus an
[Address](#address)), not the loose free-text field a box used to carry directly — that was good enough
for "which depot", not for "which shelf", which is the problem bay allocation solves.

Creating, renaming or removing a location or its bays is Administrator only: setting up a depot is
infrastructure, not day-to-day box handling.

### Bay

A 1m by 1m storage area within a [Location](#location), identified by a short code (e.g. "A3") that
only has to be unique **within its own location** — two depots may each have a bay called "A1". A bay
may hold several boxes at once; what is enforced is the other direction: a box may only be in one bay,
within one location, at a time — assigning a bay whose location does not match the box's current
`LocationId` is refused.

Placing (or moving) a box in a bay is **Loader only** — narrower even than
[box validation](#box), because this is the on-site, physical act of shelving a box so it can be found
again, not a coordination task. The system keeps a full history of a box's bay assignments (who placed
it, when, and when it moved on), mirroring the QR label's issue/revoke shape: assigning a new bay
vacates whatever bay the box was already in, as one transactional act, so a box is never recorded as
being in two bays at once.

### Receiver

*Diagrams: [Receiver registration (state)](../states/receiver-registration.puml).*

*Flows: [11 Receiver registration](../sequences/11-receiver-registration.puml) ([process](../process/11-receiver-registration.puml)).*

The destination of a box's contents: a responsible individual, an organisation, and an [Address](#address) in
Ukraine.

> **Naming:** the domain type was spelled `Reciever` and has been renamed to `Receiver` across the solution,
> along with `ResponsibleIndiviual` → `ResponsibleIndividual`.

### Manifest

**The central document of the system**, and the document *pack* for one entry on a convoy's
[Truck List](#truck-list): one vehicle, on one convoy. A manifest carries:

- the [Boxes](#box) making up the cargo,
- the border weight below,
- its GMR and ELO paperwork,
- the ferry booking status,
- and free-text delivery notes.

It is opened against that truck-list entry (`POST /convoys/{id}/vehicles/{vin}/manifest`) rather than created from
nothing, and the pair is a composite foreign key: a manifest cannot name a truck that is not on the convoy, and one
vehicle on one convoy carries exactly one manifest. That matters because [arrival](#arrival) asks each vehicle for
its finished manifest and has to get one answer.

**It carries no crew.** Who is driving is a fact about the vehicle on the convoy — see
[Vehicle Crew](#vehicle-crew) — and `GET /manifests/{id}/crew` reads it. The convoy and the vehicle are the
manifest's identity, not attributes of it, so `PUT /manifests/{id}` has no field for either: a vehicle that leaves
mid-journey is [withdrawn](#withdrawal) from the truck list, which leaves the manifest intact.

Its most important derived value is **total weight**, used for border checks. Total weight is the vehicle's kerb
weight, plus the sum of box weights, plus a fixed allowance of 200 kg (two drivers and their bags) and 45 kg
(fuel).

> **The fixed 200 kg + 45 kg padding is deliberate**, not a bug. It is the border-check estimate Ukrainian Action
> uses. Do not "correct" it without asking.

`GET /manifests/{id}/weight` also reports whether the cargo would exceed the vehicle's stated capacity — total
box weight against the vehicle's maximum cargo weight, and each box's own dimensions (set at validation) against
the vehicle's cargo space. **This is advisory only.** Nothing is ever rejected because of it, and a vehicle or box
with no capacity/dimension data recorded simply cannot be flagged. Do not turn this into an enforcement rule
without asking — it exists to help a dispatcher notice a problem before a convoy leaves, not to block one.

The manifest is what a [Border Guard](#border-guard-external) is shown, which is why the question of what
appears on it is a security question — see [Data Sensitivity](#data-sensitivity).

### Manifest Status

*Diagrams: [Manifest status (state)](../manifest-status.puml).*

The sign-off of one vehicle's load: **Proposed**, then **Approved** or **Rejected**. A rejected load is fixed and
proposed again, and **any change to an approved load returns it to Proposed** for re-approval
([P6](decisions.md#p6), [X3](decisions.md#x3)). Approval files nothing: declarations are prepared and filed
afterwards ([Customs declarations](customs-declarations.md)). Drawn in [`manifest-status.puml`](../manifest-status.puml).

> The code still implements the earlier ten-state `ManifestStatus` (`Created` to `Returned`), with approval freezing
> the manifest, until [plan 15](../plans/15-manifest-signoff-lifecycle.md) merges.

### Truck List

*Diagrams: [Truck-list entry (state)](../states/truck-list-entry.puml).*

The set of vehicles committed to a [Convoy](#convoy), produced at the start of the process and published so that
manifests can be proposed against it (see [`process.puml`](../process.puml)). One row per vehicle per convoy, and
the single statement of "this vehicle is travelling with this convoy" — the crew, the insurance and the
[Manifest](#manifest) all hang off it.

Publishing closes the list **to additions**: a vehicle cannot join afterwards, because a manifest would then be
proposed against a set that is still moving.

#### Withdrawal

Publishing does not close the list to *departures*, because vehicles break down. A vehicle that leaves the convoy
mid-journey is **withdrawn**: its entry is stamped with the time and a reason and stays on the list, along with its
crew, its insurance and its manifest. Nothing is deleted, for three reasons:

- its manifest and GMR still describe a load that is real,
- the crew that set off is the record of who went, and
- which convoy it left is part of what happened.

A withdrawn vehicle is skipped by [readiness](#readiness) and by [arrival](#arrival), and is free to join a later
convoy, or to make its own way after repair.

---

## Documents

### Manifest

See [Manifest](#manifest) above. Generated by Freedom, stored in the document store, and carried in the vehicle.

### ELO — Obligatory Logistics Envelope

*Enveloppe Logistique Obligatoire.* The barcode a driver presents at the French **Smart Border**,
required per transport unit on a UK → France RoRo crossing from April 2026. No envelope, no crossing.

**It is not a goods declaration, and this is the thing to understand about it.** An envelope carries
no description of the load, no weights, no consignor or consignee, no addresses and not even a
registration. It is an *index*: a set of flags describing the crossing, plus a list of declaration
identifiers issued by other customs systems. What is in the lorry is those declarations' business.

| Part | What it is |
| --- | --- |
| `sensTraversee` | Direction, named from the French border's side. UK → France is **`IMPORT`**. |
| `typeCamion` | `PLEIN` (loaded) or `VIDE` (empty). |
| `estTIRATA`, and five further flags | Whether TIR/ATA formalities, a transport contract, postal goods, empty packaging, sanitary goods or fishery products are involved. Each one changes what customs demands in the same envelope. |
| `identifiantsDeclaration` | The formalities being paired to this crossing — an **ENS** from ICS2, an import or transit MRN from Delta / DELTA-T. |

French Customs' guidance for this traffic, recorded in issue #24, is *"For emergency humanitarian aid
destined for Ukraine, select TIR/ATA without transport contract"* — which, under cross-functional
rule ENV_CTR_RG08, reduces the requirement to a single ENS rather than an ENS plus a
customs-clearance formality. That ruling lives in the code as
`EloCrossingProfile.HumanitarianAidToUkraine`.

An envelope belongs to one lorry, so in Freedom it belongs to one [manifest](#manifest), beside its
[GMR](#gmr--goods-movement-reference). Approving a manifest requests it; the Customs Worker obtains
it asynchronously and stores the envelope and its barcode, which `GET /manifests/{id}/elo` and
`/elo/document` read back.

Its lifecycle after creation is `FERMEE` (closed) → `APPAIREE` (paired at the port) → `EMBARQUEE` →
`DEBARQUEE`. Freedom does not follow it past `FERMEE` yet.

The declaration identifier the envelope names is the **[ENS](#ens--entry-summary-declaration)** MRN,
recorded against the manifest. Approval refuses outright if there is none, because an envelope for a
loaded lorry that names no formality is refused by French customs under ENV_CTR_RG08 — and by then the
manifest would be frozen. See [`schemas/edi/onboarding.md`](../schemas/edi/onboarding.md) for what still
has to happen on the French side: the authorisation, agreement and certification with DGDDI, none of
which is a software task.

### ENS — Entry Summary Declaration

The safety-and-security declaration the EU requires **before** goods arrive, lodged in **ICS2** (Import
Control System 2). Mandatory for road carriers since 1 September 2025, at least an hour before the
vehicle enters the EU.

It is a real declaration, unlike the [ELO](#elo--obligatory-logistics-envelope): consignor and consignee with addresses, a
plain-language description and a six-digit-minimum commodity code per goods item, package counts and
types, gross mass, the countries the goods pass through, the mode of transport and the means of
transport. ICS2 answers an accepted one with an **MRN** — eighteen characters, two digits of year, the
declaring country's ISO code, thirteen characters of reference and a check character.

**That MRN is what the ELO pairs the crossing against, so it comes first.** ENS → MRN → ELO → barcode →
check-in, with no catching up at the border.

For an **accompanied** movement only one party may lodge it, and it is the carrier — the operator of the
active means of transport. Ukrainian Action's own volunteers drive, so the charity is the filer.

Mode of transport describes the crossing rather than the vehicle: a ferry sailing is maritime (**1**), a
LeShuttle crossing is road (**3**), and rail (2) is not accepted at the Brexit Smart Border. A ferry
names the vessel's IMO as its active means of transport; the shuttle names the lorry's plate. Both, and
several other fields, are **non-amendable** — a mistake is corrected by invalidating the declaration and
lodging a new one.

**Freedom records an ENS; it does not submit one.** ICS2's Shared Trader Interface speaks eDelivery AS4,
which needs an always-on inbound access point this design declines (`recommendations.md` §4.1). So
`GET /manifests/{id}/ens/filing-sheet` composes everything a filer needs, a Ground Officer files it in
the EU Customs Trader Portal, and a Dispatcher records the MRN with `PUT /manifests/{id}/ens`. The sheet
carries no delivery address — the filer holds that already, under the one role allowed to read it — and
it reports its own gaps, so a convoy's paperwork can be fixed while there is still time. See
[`schemas/ics2/onboarding.md`](../schemas/ics2/onboarding.md) and
[`adr/0003-ens-declaration-recorded-not-submitted.md`](../adr/0003-ens-declaration-recorded-not-submitted.md).

### GMR — Goods Movement Reference

The reference used for transporting goods via the Goods Movement Service (UK Government).

There is an [API](https://developer.service.hmrc.gov.uk/api-documentation/docs/using-the-hub) available to create
it. Freedom submits GMRs asynchronously and **polls** HMRC for the outcome rather than exposing a callback
endpoint — see [recommendations §4.1](../recommendations.md#41-pull-from-hmrc-do-not-expose-a-webhook).

---

## Data Sensitivity

*Flows: [10 Leader address access](../sequences/10-leader-address-access.puml) ([process](../process/10-leader-address-access.puml)).*

Not all data in Freedom carries the same risk, and the difference drives how it is stored and who may see it.

| Class | Examples | Handling |
| --- | --- | --- |
| **Sensitive** | Ukrainian delivery addresses, receiver contact names and organisations | Segregated storage, Ground Officer access only, every read audited, redacted from anything that crosses a border |
| **Personal** | Volunteer names, dates of birth, phone numbers, driving license | UK data residency, never written to logs, defined retention period, **erased on request** (see [Volunteer erasure](#volunteer-erasure)) |
| **Operational** | Convoys, vehicles, boxes, weights, routes within the UK/EU | Standard role-based access |

**Why this matters.** A manifest listing precise Ukrainian delivery addresses is a targeting document, and it
travels in a vehicle across several borders where it may be inspected, photographed or seized. The [Ground
Officer](#ground-officer) role already exists to segregate delivery logistics from delivery detail; this
classification makes that separation explicit and enforceable rather than a matter of individual discretion.

The practical consequence: **what is on the manifest is a deliberate decision, not an accident of the data
model.** Documents that travel show cargo, weights and a region-level destination. Precise delivery detail is
given only to the Convoy Leader, never to other drivers ([O1](decisions.md#o1), [X7](decisions.md#x7)). See
[recommendations §4.4](../recommendations.md#44-treat-ukrainian-delivery-detail-as-the-most-sensitive-data-in-the-system).

The same rule applies to a [Box](#box)'s **QR label**. Its renderer (`BoxLabelRenderer`) takes a box id, a token
and a date — there is no parameter through which a receiver, region or address could reach it, so the redaction
is a property of the type rather than a rule a developer has to remember. Component and BDD tests assert the
rendered label never contains the box's city, street or receiver reference.

---

## Manifest Verification _(proposed)_

A [Border Guard](#border-guard-external) cannot be given an account, but verification currently depends entirely
on paper and conversation with the driver.

**Proposal:** a QR code on the printed manifest linking to a verification page that is scoped to that one
manifest by a signed token, valid only for the convoy window, read-only, and redacted per
[Data Sensitivity](#data-sensitivity) — showing vehicle, weights, box counts and contents categories, but no
receiver address or contact.

This would speed verification and reduce the pressure to print sensitive detail onto the manifest. **It is not a
decided feature** — it needs input from someone who has actually stood at the border.

---

## Notification

Drivers are told of their convoy allocation and given access to their manifest **on screen**. Email may be built
later ([O21](decisions.md#o21)). Notifications
carry a link to an authenticated page, never a direct document URL — documents are served through short-lived,
authorised links only.

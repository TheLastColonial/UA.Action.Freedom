# Key Concepts

The shared vocabulary for Freedom. Terms defined here should be the terms used in code, in the C4 diagrams and in
conversation with Ukrainian Action.

Related: [System Context](../c4/1-system-context.puml) · [Containers](../c4/2-containers.puml) ·
[Manifest creation process](../process.puml) · [Manifest status](../manifest-status.puml) ·
[Architecture recommendations](../recommendations.md)

---

## Operating Rhythm

Freedom is not a steady-state system. **Convoys run roughly once a month.** The month looks like a long quiet
period followed by a burst of a few days spanning manifest building, box preparation, loading and departure.

This is a domain fact with real consequences, so it is recorded here rather than buried in an architecture
document: the system is sized to cost nothing while idle and to scale up for the convoy window. Cold starts
between convoys are an accepted trade. See [recommendations §2.2](../recommendations.md#22-the-container-apps-budget-in-hours).

The **convoy window** — the days either side of departure when the system is actually in use — is therefore a
first-class operational concept, not just a date range.

---

## Roles

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

A volunteer who drives a vehicle on one leg of a convoy. Drivers are notified of their allocation and receive
their manifest, but do not administer the system. A driver may be *committed* to a convoy or merely available.

### Volunteer erasure

A volunteer who leaves can ask to be erased, and UK data protection gives them that right. Freedom **deletes their
personal data** — name, date of birth, phone, driving status — rather than hiding it. The records they were part of
(past convoy crews, manifest teams, who validated or shelved a box) keep an anonymous identity in their place and read
**"Former volunteer"**; nothing links that identity back to the person. Someone no record names is removed
outright.

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

A collection of [Vehicles](#vehicle) travelling together to Ukraine, with a departure timestamp, an expected
arrival timestamp and a [Route](#route). The convoy is the unit that is planned; the [Manifest](#manifest) is the
unit that is executed per vehicle.

#### Readiness

A convoy is **ready** when it has a route, has vehicles, and every vehicle on it is ready; a vehicle is ready with
**at least two drivers** (passengers do not count) and [insurance](#vehicle-insurance) that is recorded, not voided,
and in cover on the departure date. Readiness is **advisory** — it says what is missing and blocks nothing — and is
shown on the convoy's overview. Cargo checks will join it later.

#### Arrival

A convoy **arrives** when the Dispatcher marks it so (`POST /convoys/{id}/arrive`), which is allowed only once its
truck list is published and **every vehicle on it has a finished manifest** — Delivered, Lost or Returned. Until
then the request is refused and names the vehicles still travelling. Arrival, in one step:

- **Delivered and Lost vehicles are handed over.** A vehicle is itself part of the aid and stays in Ukraine, so it
  is stamped `HandedOverAt` and is never offered for a convoy again.
- **Returned vehicles are released** — taken off the convoy so they can travel again.
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

The people travelling in a [Vehicle](#vehicle) on one [Convoy](#convoy), decided while it is planned — before any
[Manifest](#manifest) exists for the leg. Each crew member is either:

- a **Driver** — a volunteer registered to drive; or
- a **Passenger** — any volunteer.

**A person takes one seat per convoy**: they cannot be on two vehicles of the same journey (the database enforces
it). A vehicle needs **two drivers** to be [ready](#readiness) — the norm for sustained driving and border
compliance; passengers do not count. Crewing is the [Dispatcher](#dispatcher)'s alone.

The crew can still change after the truck list is published — a driver falls ill — but **any change voids the
vehicle's [insurance](#vehicle-insurance)**, which names the crew, and it must be recorded again before the vehicle
departs. Once the convoy has [arrived](#arrival) the crew is history and cannot change.

### Vehicle Insurance

Bought by the Dispatcher for each vehicle on a convoy, and it **names that vehicle's crew**. Recorded per vehicle
per convoy: insurer, policy number, cover start and end, optional cost, and who recorded it (taken from their login,
never typed in). Dispatcher and Administrator may record it (`PUT /convoys/{id}/vehicles/{vin}/insurance`).

- **A crew change voids it**, in the same step as the change. Recording it again renews it.
- **A manifest cannot depart without it** — recorded, not voided, and in cover on the day — and is refused with the
  reason.
- Taking the vehicle off the convoy, or cancelling the convoy, removes it.

This is distinct from a [Driver Team](#driver-team): the crew is a property of the vehicle within the convoy,
decided during planning, while a Driver Team is a primary/secondary pair fixed to one specific leg once a manifest
exists for that vehicle.

### Route

The ordered list of [Addresses](#address) a convoy will pass through, from UK departure to Ukrainian delivery.

### Address

A location in the real world. Note that not all addresses are equally sensitive: a UK depot is routine, a
Ukrainian delivery address is not. See [Data Sensitivity](#data-sensitivity).

### Item

A single donated thing, with a description and open-ended properties. Items are not tracked individually in
transit — they are tracked as the contents of a [Box](#box).

### Box

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

The destination of a box's contents: a responsible individual, an organisation, and an [Address](#address) in
Ukraine.

> **Naming:** the domain type was spelled `Reciever` and has been renamed to `Receiver` across the solution,
> along with `ResponsibleIndiviual` → `ResponsibleIndividual`.

### Driver Team

A pair of drivers — a primary and a secondary — allocated to one leg of a journey. A [Manifest](#manifest)
carries two teams: `DriverUK` for the UK→Europe leg and `DriverBorder` for the Europe→Ukraine leg.

See [Vehicle Crew](#vehicle-crew) for the earlier, convoy-planning-stage assignment this narrows down from.

### Manifest

**The central document of the system.** A manifest ties together, for one vehicle on one convoy:

- the [Vehicle](#vehicle) carrying the load,
- both [Driver Teams](#driver-team),
- the [Boxes](#box) making up the cargo,
- the ferry booking status,
- and free-text delivery notes.

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

The lifecycle a manifest moves through. `ManifestStatus` is a ten-state enum — `Created, Proposed, Rejected,
Confirmed, Preparing, Ready, InTransit, Delivered, Lost, Returned` — kept in sync with
[`manifest-status.puml`](../manifest-status.puml) edge-for-edge; the allowed transitions live as data in
`ManifestTransitions.CanTransition` (`Manifest.cs`), pinned by
`tests/UA.Action.Freedom.Tests.Unit/Domain/ManifestTransitionsTests.cs`. The happy path is linear; the only
backward edge is `Rejected → Proposed`. GMR submission is triggered from the `Confirmed → approve` transition,
which freezes the manifest in the same statement that stamps the GMR timestamp — see CLAUDE.md's manifest
lifecycle section for the freeze semantics.

### Truck List

Produced at the start of the process: the set of vehicles committed to the next convoy, published so that
manifests can be proposed against it. See [`process.puml`](../process.puml).

---

## Documents

### Manifest

See [Manifest](#manifest) above. Generated by Freedom, stored in the document store, and carried in the vehicle.

### ELO — Obligatory Logistics Envelope

The documents used for the transportation of goods from the UK to the EU.

### GMR — Goods Movement Reference

The reference used for transporting goods via the Goods Movement Service (UK Government).

There is an [API](https://developer.service.hmrc.gov.uk/api-documentation/docs/using-the-hub) available to create
it. Freedom submits GMRs asynchronously and **polls** HMRC for the outcome rather than exposing a callback
endpoint — see [recommendations §4.1](../recommendations.md#41-pull-from-hmrc-do-not-expose-a-webhook).

---

## Data Sensitivity

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
released to the driver at the point of delivery. See
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

Drivers are told of their convoy allocation and given access to their manifest by email or SMS. Notifications
carry a link to an authenticated page, never a direct document URL — documents are served through short-lived,
authorised links only.

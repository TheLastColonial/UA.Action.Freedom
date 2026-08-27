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
paperwork. Also the booking of hotel accomidation for the trip. They will also coordinate with third parties to ensure servicing process.
Also to find insurance for the [Driver](#driver) and [Vehicle](#vehicle).

### Loader

Responsible for being on site, verifying the contents of donations and loading the vehicles in the next convoy.
Contents must be verified to weigh them for border checks and to assure the contents, as this is a trust
boundary between the donor and Ukrainian Action.

### Purchaser

Responsible for the sourcing of vehicles, equipment and other sundries consumed in a convoy.
Including transportation of the purchase to the logistical hubs.

### Ground Officer

Responsible for communicating with the local authorities in Ukraine:

- Ensuring requests for aid are collected and documented.
- Ensuring aid that is delivered is accepted.
- **Segregating the delivery logistics from the sensitive details of a delivery** — see [Data Sensitivity](#data-sensitivity).

The Ground Officer is the only role that sees full [Receiver](#receiver) detail.

### Driver

A volunteer who drives a vehicle on one leg of a convoy. Drivers are notified of their allocation and receive
their manifest, but do not administer the system. A driver may be *committed* to a convoy or merely available.

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

### Vehicle

A truck or car that has been donated, and which is itself part of the aid — vehicles are handed over in Ukraine,
not driven back. Identified by VIN and licence plate, and carrying the detail a border check needs: kerb weight,
fuel type, transmission, year, and condition notes.

> **Naming:** the domain type has been renamed from `Veichle` to `Vehicle`. At the time of writing the rename is
> incomplete — `Manifest.cs` and `Convoy.cs` still reference the old spelling. Use **Vehicle** in all new work.

### Route

The ordered list of [Addresses](#address) a convoy will pass through, from UK departure to Ukrainian delivery.

### Address

A location in the real world. Note that not all addresses are equally sensitive: a UK depot is routine, a
Ukrainian delivery address is not. See [Data Sensitivity](#data-sensitivity).

### Item

A single donated thing, with a description and open-ended properties. Items are not tracked individually in
transit — they are tracked as the contents of a [Box](#box).

### Box

A packed container of [Items](#item) with a confirmed weight, a current location, and a target
[Receiver](#receiver). A box is **validated** when a [Loader](#loader) has confirmed its contents and weight;
the system records who validated it and when.

Validation is the trust boundary between the donor and Ukrainian Action, and the weight it produces is what the
border check relies on. Both facts make the validation record an audit artefact, not just a status flag.

### Receiver

The destination of a box's contents: a responsible individual, an organisation, and an [Address](#address) in
Ukraine.

> **Naming:** the domain type is currently spelled `Reciever`. Prefer **Receiver** in documentation and in new
> code.

### Driver Team

A pair of drivers — a primary and a secondary — allocated to one leg of a journey. A [Manifest](#manifest)
carries two teams: `DriverUK` for the UK→Europe leg and `DriverBorder` for the Europe→Ukraine leg.

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

The manifest is what a [Border Guard](#border-guard-external) is shown, which is why the question of what
appears on it is a security question — see [Data Sensitivity](#data-sensitivity).

### Manifest Status

The lifecycle a manifest moves through. **There is a known inconsistency**: the code defines six states
(Proposed, Confirmed, Prepared, Transit, Arrived, Lost) while [`manifest-status.puml`](../manifest-status.puml)
documents ten (adding Created, Rejected, Ready, Delivered, Returned) with different transitions.

Treat the `.puml` as the intended target design and the code as not yet caught up. This needs resolving before
the workflow is built, because the status model determines where GMR submission is triggered from.

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
| **Personal** | Volunteer names, dates of birth, phone numbers, driving license | UK data residency, never written to logs, defined retention period |
| **Operational** | Convoys, vehicles, boxes, weights, routes within the UK/EU | Standard role-based access |

**Why this matters.** A manifest listing precise Ukrainian delivery addresses is a targeting document, and it
travels in a vehicle across several borders where it may be inspected, photographed or seized. The [Ground
Officer](#ground-officer) role already exists to segregate delivery logistics from delivery detail; this
classification makes that separation explicit and enforceable rather than a matter of individual discretion.

The practical consequence: **what is on the manifest is a deliberate decision, not an accident of the data
model.** Documents that travel show cargo, weights and a region-level destination. Precise delivery detail is
released to the driver at the point of delivery. See
[recommendations §4.4](../recommendations.md#44-treat-ukrainian-delivery-detail-as-the-most-sensitive-data-in-the-system).

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

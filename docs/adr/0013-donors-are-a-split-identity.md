# 13. Donors are a split identity, so they can be erased, and a donation is its own entity

Date: 2026-10-02

## Status

Accepted. Implemented by [plan 06](../plans/06-donors-donations.md): `dbo.Donor`, `dbo.DonorDetail` and `dbo.Donation`, `BoxItem.DonationId`, the `/donors` and `/donations` API with its three policies, and the donor status report. The repository does not refuse erasure, as decided here.

Implementation note: the report filters out voided boxes only once [plan 16](../plans/16-box-replacement-label.md) adds voiding. There is no voided state to filter today.

## Context

An item has a description and free properties and, today, **no origin**. Nothing says who gave it. Product
discovery needs that for three reasons: the charity reports what was donated, by whom and to what effect; a donor
may be told, at a high level, what became of what they gave; and a donation is a real event, one donor and one
drop-off, that arrives as a group of items.

A donor is **personal data**, and UK data protection gives them the right to be erased. Freedom has already solved
that problem once, for volunteers (the `Person`/`PersonDetail` split): `dbo.Person` is an anonymous key
(`Id`, `CreatedAt`, `ErasedAt`) that other records point at, and the personal data lives in `dbo.PersonDetail`.
Erasure deletes the detail row and removes the identity too, **unless a past record names it**, in which case it is
stamped `ErasedAt` and those records read **"Former volunteer"**.

Donors have no access to the system. A donor tells HQ by email that a box is coming, and a Dispatcher or Loader
may enter it ([decision O22](../domain/decisions.md#o22)).

## Decision

### A `Donation` is its own entity

One donor, one drop-off or consignment, many items. **Items belong to a donation**
([decision D14](../domain/decisions.md#d14)), and so every item is attributed to the donor who gave it
([decision D9](../domain/decisions.md#d9)). Entering a donation early, before the box arrives, is optional.

### Donors follow the volunteer pattern

An **anonymous key** that records and reports refer to, and the **personal details held separately**, so they can
be erased ([decision D15](../domain/decisions.md#d15)). Erasing a donor deletes the details and keeps the counts
and values in reports. The donation, its items and its value survive.

After erasure the donor appears as **"Former donor"** on documents already issued
([decision D28](../domain/decisions.md#d28)).

### The donor status report is a user's report, not a donor's view

A **high-level report** shows the items a donor has given and their status
([decision O6](../domain/decisions.md#o6)). It is produced for the donor **by a user**, and the donor has no
login. It is a report over the donor's donations, and shows no Receiver, route or address.

### What is not a donation

Things the charity buys are not donations, and have no donor: **vehicle equipment** such as warning triangles
([O13](../domain/decisions.md#o13)), and vehicles it pays for ([O11](../domain/decisions.md#o11)). They are not part
of the value delivered.

## Alternatives considered

**Store the donor's name on the item.** Cheapest, and impossible to erase without editing every item the donor
ever gave, including those inside attested boxes that are never edited
([ADR 0011](0011-attested-boxes-are-replaced-not-edited.md)).

**Hide a donor instead of deleting.** Rejected for the same reason as for volunteers: hiding is not erasure. The
data must actually be deleted.

**A donor portal.** Out of scope. A thank-you when items arrive at their destination may be explored later
([O22](../domain/decisions.md#o22)), and the focus is boxes and vehicles reaching their destinations.

## Consequences

**Reports must tolerate an erased donor.** Value and count reports group by the anonymous key, so an erasure never
changes a total. Only the displayed name changes, to "Former donor".

**A donation must not be lost with its donor.** The `Donation` and its items cascade from nothing the erasure
touches, and the detail table is the only thing deleted, which is how `dbo.PersonDetail` works.

**Is erasure ever refused?** For a volunteer it is refused while they are still needed on a convoy that has not
arrived. A donor has no operational dependency, so the natural answer is that it is never refused. That has not
been confirmed, and it interacts with how long records must be kept
([Q-retention](../domain/decisions.md#q-retention)).

**Entering a donor is staff handling personal data.** A Dispatcher or Loader types a donor's details from an
email. That is a processing activity with a lawful basis and a privacy notice, which is a charity decision, and
this ADR does not make it.

**A second place a person's data lives.** `Person` and `Donor` are different things and should stay different:
a volunteer can also donate, and erasing one must not erase the other.

# 06. Donors and donations

| | |
|---|---|
| **Branch** | `feat/donors-donations` |
| **Covers** | [ADR 0013](../adr/0013-donors-are-a-split-identity.md); [D9](../domain/decisions.md#d9), [D14](../domain/decisions.md#d14), [D15](../domain/decisions.md#d15), [D28](../domain/decisions.md#d28), [O6](../domain/decisions.md#o6), [O22](../domain/decisions.md#o22) |
| **Depends on** | [05](05-item-classification-value.md) |
| **Gate** | None |
| **Flows** | [02 Donation and box intake](../sequences/02-donation-and-box-intake.puml) ([process](../process/02-donation-and-box-intake.puml)) |

## Context

Items have no origin today. ADR 0013 adds a `Donation` (one donor, one drop-off, many items) and a **donor as a split
identity**, so a donor can be erased without losing the donation, its items or its value. The pattern already exists
for volunteers:
- `dbo.Person` (`Id`, `CreatedAt`, `ErasedAt`) and `dbo.PersonDetail`, which cascades from it;
- `PersonRepository.AddAsync` (l.69–89) inserts both in one transaction;
- `PersonRepository.DeleteAsync` (l.115–185): an `UPDLOCK` check, delete the detail, try to delete the identity, and
  on a foreign-key violation (547) stamp `ErasedAt`.

Rules: [Boxes and donations § Donations and donors](../domain/boxes-and-donations.md#donations-and-donors).

### Decisions this plan relies on

| Decision | Effect here |
|---|---|
| [D14](../domain/decisions.md#d14), [D9](../domain/decisions.md#d9) | `Donation` entity. Every item belongs to a donation. |
| [D15](../domain/decisions.md#d15), [D28](../domain/decisions.md#d28) | `Donor` (anonymous key) and `DonorDetail` (erasable). After erasure the donor reads "Former donor". |
| [O6](../domain/decisions.md#o6) | A high-level donor status report, produced by a user. It shows no Receiver, route or address. |
| [O22](../domain/decisions.md#o22) | A Dispatcher or Loader enters a donation, optionally before the box arrives. |
| Plan decision | `DonorDetail` lives in **`dbo`**, mirroring `PersonDetail`. Donor data is personal data, not Ukrainian delivery detail. |

## Increments

### Increment 1: split identity in the domain and schema

- **Domain:** `Donor`, `DonorDetail` (name, email, phone) and `Donation` (donor, received date, notes).
- **Schema:**
  - `dbo.Donor (Id uniqueidentifier, CreatedAt, ErasedAt)`;
  - `dbo.DonorDetail` (cascades from Donor);
  - `dbo.Donation (Id int identity, DonorId, ReceivedOn, …)`;
  - `dbo.BoxItem` gains `DonationId int NULL` (a foreign key).
- Every new table carries `LastChangedBy` and `LastChangedAt` (the [plan 03](03-last-changed-audit.md) schema guard
  will fail otherwise).
- **Tests (RED first):** integration tests that adding a donor writes both rows in one transaction.

### Increment 2: erasure

- `DonorRepository.DeleteAsync` copies the `PersonRepository` pattern:
  - delete the detail;
  - try to delete the identity;
  - on a foreign-key violation, stamp `ErasedAt`.
- **Erasure is never refused**: a donor has no operational dependency (ADR 0013). Record that in the PR.
- **Display:** a `dbo.DonorDisplay` view, or the same helper approach as `PersonDisplay`, shows "Former donor" for an
  erased donor.
- **Tests:** integration tests for erasing an unreferenced donor (row deleted) and a referenced donor (stamped, items
  keep their donation). Mirror both in the fake.

### Increment 3: donations and their items

- **Application:** `Application/Donations/` with create, update, get, list and delete donation; add an item to a
  donation; and list a donor's donations.
- **Api:**
  - `/donors` CRUD and `/donations`;
  - a new policy `donations:write` for the Dispatcher, Loader and Administrator ([O22](../domain/decisions.md#o22));
  - `donations:read` for all operational roles;
  - donor erasure (`DELETE /donors/{id}`) for the **Administrator only**, as for volunteers.
- Adding a box item takes an optional `DonationId`.
- **Tests:** component tests for the policies and JSON contracts, and integration tests.

### Increment 4: donor status report

- `GET /donors/{id}/report`: the donor's donations, item counts and value by category, and each item's box status, at
  a high level.
- **No Receiver, route, region or address**, and no other donor.
- **Tests:** a component test that asserts **the absence** of receiver, region, route and address fields, in the style
  of the label tests. A unit test that a voided box is excluded (no-op until [plan 16](16-box-replacement-label.md)
  adds voiding, but write the filter now).

### Increment 5: web

- Donor list, detail and form; donation form; "add items from a donation" on the box items panel; a report view with
  print styles.
- An erased donor renders as "Former donor".
- Tests: Vitest with MSW; Playwright smoke for creating a donation and adding its items to a box.

### Increment 6: seed and BDD

- Seed fictional donors and donations. Point seeded items at them.
- **BDD:** `Features/Donations.feature`: record a donation, add its items to a box, erase the donor and see "Former
  donor", and the report has no Receiver.

## Retires and transitional

- **Transitional:** existing items have no donation, so `DonationId` is nullable. The data-quality warning "an item has
  no donor" ([P17](../domain/decisions.md#p17)) comes with [plan 13](13-readiness-departure.md).

## Docs to update

- `CLAUDE.md`: a new Donations slice in Architecture, and a note that donors use the split-identity pattern.
- `README.md`: endpoints and the policy matrix.
- `docs/domain/key-concepts.md` § Donor: now an entity, not only external. Remove the row from the amendments table.
- ADR 0013: an implementation note.
- `web/src/auth/policyMatrix.ts`.
- Gotchas: [Q-retention](../domain/decisions.md#q-retention) is still open.

## Risks

- **A volunteer can also be a donor.** `Person` and `Donor` must stay separate, so erasing one never erases the other.
  Add a test.
- **Processing personal data.** Entering a donor's details from an email is a data-protection activity. The lawful
  basis and privacy notice are the charity's decision. Note it in the PR; do not decide it in code.

## Testing

All four .NET layers, the web pages, and BDD for the donation flow and erasure.

## Verification

The standard gates, plus on the local stack: record a donation, attach items, view the report, erase the donor, and
check that the report and box items show "Former donor" and that totals are unchanged.

## Sequencing

After [plan 05](05-item-classification-value.md). Nothing depends on it, so it can be moved later if the critical path
needs attention.

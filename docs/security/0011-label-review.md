# Data-sensitivity review: the box label that lists contents and the signer

Date: 2026-10-04. Gate for [plan 16](../plans/16-box-replacement-label.md), Increment 0, and for
[ADR 0011](../adr/0011-attested-boxes-are-replaced-not-edited.md) ([D2](../domain/decisions.md#d2),
[O17](../domain/decisions.md#o17)). Status: **proposed, awaiting owner sign-off**. Nothing in the label has changed
yet.

Reviewed against [Data Sensitivity](../domain/key-concepts.md#data-sensitivity) and
[recommendations section 4.4](../recommendations.md#44-treat-ukrainian-delivery-detail-as-the-most-sensitive-data-in-the-system).

## 1. What the label shows today and what it would show

| Field | Today | Proposed (D2, O17) | Classification |
|---|---|---|---|
| Charity name | yes | yes | Public |
| Box number | yes | yes | Operational |
| Issue date | yes | yes | Operational |
| QR code (opaque token, resolves to the record behind a login) | yes | yes | Operational. The token is a random GUID, non-enumerable. |
| Item categories | no | yes, English and Ukrainian | Operational |
| Item quantities | no | yes | Operational |
| Item free-text descriptions (`BoxItem.Description`) | no | **open question, see 4** | Operational, but **unconstrained text** |
| Item properties (`PropertiesJson`, string map) | no | **no, recommend never** | Operational, unconstrained |
| Signer's name (`ValidatedByPersonId` through `dbo.PersonDisplay`) | no | yes (form open, see 3) | **Personal data** (UK GDPR) |
| Item value in GBP, donor, donation | no | **no, recommend never** | Operational and donor-linked |
| Expiry dates | no | optional, recommend yes for medicine and food | Operational |
| Receiver, region, address, contact | no | **no, structurally impossible** | **Sensitive** |

## 2. Who can read a label at each border

A label is physical. Anyone who can see the box can read it, and the charity cannot control that. Assume
**every reader at every checkpoint can photograph it and may keep the photograph.**

| Reader | Sees the label | Can use the QR | Notes |
|---|---|---|---|
| UK exit and HMRC officers | yes | no, resolves only behind a Freedom login | Expected audience. Contents are what they check. |
| French and EU customs, ferry and port staff | yes | no | Same. |
| Polish / Slovak / Romanian / Moldovan and Ukrainian border officials | yes | no | Ukrainian officials are the intended recipients of the attestation. |
| Any hostile or third-party inspector, or a seized vehicle | yes | no | **The threat case.** Contents and signer's name become known to someone who may be hostile. |
| Warehouse and convoy volunteers | yes | yes with an operational login | `GET /boxes/scan/{token}` is `boxes:read`. |
| Ground Officer | yes | not for boxes | Excluded from operational policies by design. |

Contents are information about the load, not the destination. They are already written on the manifest and the
customs declarations, which cross the same borders. **The label adds no category of information that a border
official does not already receive from the paperwork.** What is new is that it is **bound to the physical box**,
which is the point of D2, and that it is **retained wherever a box is left, lost or seized** rather than only with
the driver's paperwork.

The QR code, deliberately, still reveals nothing without an authenticated, role-checked request. Revoking a code (void
or reprint) makes the old label resolve to 404, so an old photograph stops working.

## 3. The signer's name

The label would carry the **name of the volunteer who attested the box**. That is a living person's real name on an
object that crosses a conflict-adjacent border, may be photographed, and may be seized. Volunteers on this
charity's convoys are named, UK-based people who also drive vehicles into Ukraine.

Risks:

- Personal data on a physical object **cannot be erased** on request. [Volunteer erasure](../domain/key-concepts.md#volunteer-erasure)
  deletes the detail row, but a printed label still carries the name. A UK GDPR erasure request after the box has
  shipped is not satisfiable for the paper.
- A hostile reader links a named UK individual to aid deliveries into Ukraine, and the name appears on every box that
  person signed.
- A full name on the label has no operational benefit that initials plus a volunteer number would not also give: the
  point is that **a named, accountable person vouched**, and a border official can ask the driver, who holds the
  manifest, who that is.

Options:

| Option | Accountability | Exposure | Fits erasure |
|---|---|---|---|
| A. Full display name (`First Last`) | Highest | Highest | No |
| B. First name and last initial (`Alex E.`) | High enough to ask "who is that" | Lower | Not fully |
| C. A **signer code** (a stable, non-guessable volunteer number or short code that Freedom resolves) | The charity can always identify, a stranger cannot | **Lowest** | **Yes**: erasing the volunteer breaks the link, the label keeps a meaningless code |
| D. Initials only | Weak | Low | Partly |

**Recommendation: C (a signer code), shown with the attestation date.** If the owner needs a human-readable name for
border confidence, **B**. Avoid A. Whichever is chosen, an **erased volunteer prints as "Former volunteer"**, as
`dbo.PersonDisplay` already decides for every other read, and the label must use that view and never `PersonDetail`
directly.

The attestation is already stored with `ValidatedByPersonId` in Freedom, so the full accountability record is
unchanged by what the label prints.

## 4. Free text, categories and anything else that could carry sensitive information

**Category names.** The fixed list is Medicine, Food, Clothing, Hygiene, Medical devices, Tools, Batteries, Gas,
Flammables, Other. None is sensitive in the sense of this system (not an address or a person). Two are
**operationally sensitive** because they draw closer inspection: Medicine (`IsSensitive`) and the not-carried goods
(Batteries, Gas, Flammables). A label that advertises "Medicine" on a box invites theft or a targeted search, and a label
that advertises "Batteries" or "Gas" is a regulatory flag. But the goods are declared on the customs paperwork
anyway, and a box that contains them and is labelled otherwise would be a false attestation. **Recommendation: show
categories honestly.** The owner should confirm that is the intent, and may wish to treat Medicine as category-only
(no drug names, no quantities per drug) on the label.

**Free-text `Description` and `Properties`.** This is the real exposure. These fields are typed by a Loader, are not
constrained to a vocabulary, and today have **never appeared on anything that travels**. A Loader could write
"for the 3rd brigade, Kharkiv", a receiver's name, a donor's name or a person's phone number, and the label would
print it. That would defeat the structural rule that a label carries no receiver, region or address, because **the
type guarantee only covers the parameters, not the text inside a free-text parameter.**

Recommendation, in order of safety:

1. **Print categories and quantities only.** No free text on the label. Item detail stays in the system and on the
   manifest. Simplest, safest, and removes the translation-quality problem in the
   [spike](../spikes/0011-offline-translation.md) entirely.
2. If the owner wants descriptions: print `Description` only after a Loader has confirmed the English and Ukrainian
   lines at attestation, **never `Properties`**, and add a **length and character limit plus a screening check**
   (no digits runs that look like phone numbers, no names from the donor and receiver tables) on the item's
   description at write time, with a warning. A screen reduces but cannot eliminate the risk, so this is weaker than 1.
3. Never print value in GBP, donor, donation id or receiver data. Value and donor are commercially and personally
   linked, and the donor report already withholds route, receiver, region and address by type.

**Attestation text.** The Ukrainian translation is unmarked (O29). A mistranslation on the label is read as the
charity's attestation. See the spike: about half of free-text test lines came out wrong. This is a **correctness and
trust** risk rather than a confidentiality one, and is an additional reason for option 1.

## 5. The receiver, region and address stay structurally impossible

Confirmed as a design constraint, not only a test:

- `BoxLabelRenderer.ToSvg(int boxId, Guid token, DateTime issuedAt, string baseUrl)` has no receiver, region or
  address parameter, and `Box` read models on the label path are not given one. `BoxReadModel` carries a
  `ReceiverRef` (a GUID) but `ReceiverReadModel` has no address or contact (it has only status), and a **Ground
  Officer alone** can read an address through the separate `ISensitiveDbConnectionFactory`.
- **The new signature must keep that property.** It may gain `IReadOnlyList<LabelItemLine> items` and a `signer`, where
  `LabelItemLine` carries only category name (English, Ukrainian), quantity and, if option 2 above is chosen, the confirmed
  description pair. It must **not** accept a `BoxReadModel`, a `BoxItemReadModel` or a `Dictionary<string,string>`,
  because those carry `ReceiverRef`, `DonationId`, `ValueGbp` and arbitrary properties, and passing one would
  re-open exactly the door the type is closing.
- A **signer's identity must come from `dbo.PersonDisplay`, mapped to a single label string by the caller**, so the renderer
  never sees a `PersonId`.
- Keep the existing assertions that the rendered label never contains the receiver or the location
  (`BoxQrCodeEndpointTests`, `BoxQrRendererTests`, `BoxQrCodes.feature`), and add **a test with a deliberately
  hostile item description** that includes a street and a receiver reference, proving the chosen option stops it
  (trivially true under option 1).
- Telemetry rule from `CLAUDE.md` applies: no item text, signer or box content as a metric tag, span attribute or log
  field, in the translator adapter either.

## 6. Other observations

- **Replacing a box prints a new label, and the old token returns 404** (D3). A photograph of the old label becomes
  useless as a QR, but it still shows the old contents and signer. That is accepted: it records a historic attestation.
- **Translations cached at attestation** (spike mitigation 3) would also freeze what the label said, which matters when a
  border official holds a printed label and the system is asked later "what did we tell them".
- **Retention.** A signer code (option C) is the only option that survives an erasure request without leaving
  personal data on a shipped object.
- This review does not change the **manifest**, which already shows cargo, weights and region-level destination by
  design ([O1](../domain/decisions.md#o1)). Note that the manifest carries a *region*, and the label never will.

## 7. Decisions needed from the owner

1. **Signer on the label:** signer code (recommended), first name plus initial, or full name. Record the choice in
   ADR 0011.
2. **Free text:** categories and quantities only (recommended), or confirmed descriptions with screening. Never
   `Properties`, value or donor.
3. **Medicine:** is "Medicine" shown as a category on the label, or is it replaced by a neutral label such as
   "Medical supplies"? The same question for Batteries, Gas and Flammables is moot while those are not carried.
4. **Expiry dates** on the label for Medicine and Food: yes or no.
5. **Confirm the label is the same wherever the box is** (no per-border variant), and that anyone who can see the box may
   read it.
6. **Sign off** the renderer rule in section 5: the renderer takes a purpose-built label line type, never a box or item
   read model.

## 8. Owner decisions (2026-10-04)

The owner signed off the gate (PR #61), with these answers to section 7.

| # | Decision |
|---|---|
| 1 | The label identifies the signer by **both a signer code and first name with last initial** (for example `V-3F9A-21C4 Alex E.`). An erased volunteer reads "Former volunteer" and keeps the code. |
| 2 | The label prints an **itemised list**, one line per item: **category and quantity**. Never properties, value, donor or donation. The free-text screening rule stands: free text is not printed, and if a description is ever printed it is only after the attesting Loader confirms it. |
| 3 | **Medicine appears as its category**, and **expiry dates are printed** where an item has one. This is the owner's reading of the question. |
| 4 | The renderer takes a purpose-built **label-line type, never a box or item read model**. Accepted, "not sure but yes": flagged for revisit ([Q-label-renderer-type](../domain/decisions.md#q-label-renderer-type)). |

**Flags raised by this review against those answers:**

- **Printing a name (decision 1) departs from the recommendation** in section 3, which preferred a signer code alone
  because a printed name cannot be erased from a box that has shipped. The owner accepted the trade, with the name
  reduced to a first name and last initial. Recorded as
  [Q-label-signer-erasure](../domain/decisions.md#q-label-signer-erasure) to revisit with retention.
- **Medicine and expiry dates on the label (decision 3)** make the load slightly more legible to a hostile reader (a
  medicine category and a date window). Nothing here is an address or a person, the same information is on the customs
  paperwork, and the privacy review does not object. Flagged only because the owner asked for it to be.
- **Signer code:** derived, not stored. It is a one-way hash of the volunteer's random `PersonId`, so it needs no new
  column, is stable across reprints, cannot be guessed, and survives an erasure (the anonymous identity key is kept).
  It is a pseudonym, not anonymous data, and it can only be resolved inside Freedom.
- **Ukrainian wording:** the label prints the category's `NameUk` (Administrator-edited). Where it is empty the line
  falls back to the English name, so a label is never blank in the Ukrainian column. No machine translation is wired
  (see the spike).

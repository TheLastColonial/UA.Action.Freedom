# Filing an ICS2 Entry Summary Declaration as Ukrainian Action

None of this is a software task. It is an EU registration, a set of certificates and a conformance run
with the Commission, and the lead time is measured in weeks rather than days — and unlike the ELO, one
of the prerequisites may not be obtainable by a UK-established charity at all without help.

Freedom deliberately does **not** submit declarations. See
[`docs/adr/0003-ens-declaration-recorded-not-submitted.md`](../../adr/0003-ens-declaration-recorded-not-submitted.md)
for why; the short version is that the Shared Trader Interface speaks eDelivery AS4, which needs a
permanently reachable inbound access point, and `docs/recommendations.md` §4.1 declines exactly that.
A Ground Officer files in the EU Customs Trader Portal and a Dispatcher records the MRN.

Sources: the Commission's [ICS2 pages](https://taxation-customs.ec.europa.eu/customs/customs-security/import-control-system-2_en)
and its FAQ, and the technical library in the **EU Advance Cargo Information System (ICS2)** CIRCABC
group, `18fb5859-3970-4ac5-b30b-6604977a15a7`. Start with `docs/domain/key-concepts.md` § ENS for what
a declaration *is*.

## Why this is needed at all

| | |
| --- | --- |
| **Obligation** | Road carriers have had to lodge a complete ENS since **1 April 2025**, mandatory without derogation from **1 September 2025**. |
| **Timing** | At least **one hour** before the vehicle enters the EU — but in practice far earlier, because the MRN has to exist before the ELO, which has to exist before check-in. |
| **Who files** | The carrier: the operator of the active means of transport. For an **accompanied** movement only one party may lodge it, so for these convoys that is Ukrainian Action, whose own volunteers drive. There is no forwarder to defer to. |
| **What it unlocks** | The MRN is the single formality the ELO envelope needs under `ENV_CTR_RG08`, because the crossing is declared TIR/ATA. No MRN, no envelope, no crossing. |

## What has to be true before a real declaration can be filed

| # | What | Who | Blocking? |
| --- | --- | --- | --- |
| 1 | An **EU-issued EORI** for the declarant | An EU member state's customs authority — **see the open question below** | Yes |
| 2 | A **UUM&DS** registration for that EORI | The member state's National Service Desk | Yes |
| 3 | The **`STISTP_EXECUTIVE`** business profile (and `STISTP_CONFIGURATOR` to set things up) | Granted through UUM&DS by the National Service Desk | Yes |
| 4 | A rehearsal in the **conformance** environment | Us, with the filer | No, but do it |
| 5 | The consignee's delivery address, per consignment | The Ground Officer, from `GET /receivers/{ref}/detail` | Yes |

Only the last is Freedom's, and it is deliberately not automated — see § The address, below.

### 1. An EU-issued EORI — the open question

**The declarant's EORI must be issued by an EU member state. A GB EORI is not accepted.**

Ukrainian Action is UK-established, and the Commission's FAQ addresses only operators "established in
EU Member States", who obtain an EORI from the country they are established in. It does not say what a
non-EU carrier must do. The realistic answers are:

- register for an EORI in the member state of first entry (France) as a non-established person, which
  is ordinarily possible but has to be confirmed; or
- act through an **EU-established representative**, who becomes declarant; or
- engage an **IT Service Provider** (ITSP), who becomes the technical sender under their own EORI and
  certificate while Ukrainian Action remains the declarant.

**This has to be settled with French customs (DGDDI) or a customs agent before a convoy relies on it.**
It is the one prerequisite that could turn the whole approach from "file in the portal" into "engage a
provider" — and if it does, `IEnsDeclarationStore` is the seam that absorbs it without the rest of
Freedom changing.

### 2 and 3. UUM&DS and the portal roles

UUM&DS (Uniform User Management and Digital Signatures) is how the Commission identifies an economic
operator. Registration is done through the member state's own authentication route and then the
National Service Desk grants the ICS2 Shared Trader Portal business profiles. Nothing about this is
automatable and nothing about it is quick.

Note what is **not** required for portal filing: the TLS and eIDAS sealing certificates. Those are for
a system-to-system AS4 connection, which Freedom does not make. If an ITSP is engaged they hold them.

### 4. Environments

| | |
| --- | --- |
| **Conformance** | `https://conformance.customs.ec.europa.eu/euctp` |
| **Production** | `https://customs.ec.europa.eu/gtp/` |

A **self-conformance test** is mandatory before a system-to-system connection goes live; for portal
filing it is not, but rehearsing a full declaration in conformance before a convoy depends on one is
worth the afternoon. The relevant library documents are the *Conformance Test Organisation Document
(CTOD) for EO for Release 3* and the *Test Design Specifications for Economic Operator Conformance Test
Cases*.

## What Freedom gives the filer

`GET /manifests/{id}/ens/filing-sheet` (`manifests:read`) composes everything the portal asks for that
Freedom can know:

| Block | Where it comes from |
| --- | --- |
| Declarant and carrier EORI | `Customs:HaulierEori` |
| Consignor | `Ens:ConsignorName` |
| Customs office of first entry | `Ens:OfficeOfFirstEntry` — `FR620001` is Calais |
| Mode of transport | `dbo.Convoy.CrossingMode`: **ferry → 1 (maritime)**, **shuttle → 3 (road)**. Rail (2) is not accepted at the Brexit Smart Border |
| Active means of transport | The vessel IMO on a ferry, the lorry's plate on the shuttle |
| Passive means of transport | The lorry's plate, always |
| Countries of routing | `dbo.ConvoyRouteStop.CountryCode`, in journey order, de-duplicated |
| Goods items | `dbo.BoxItem.Description` and `CommodityCode`, grouped per consignee and numbered from one |
| Packages | One per box, UN/ECE Rec 21 type `BX` |
| Gross mass | `ManifestWeight.Total` — vehicle + cargo + 200 kg crew and bags + 45 kg fuel |
| Consignee | Organisation and region **only** |

and a `missing` list naming what is not there yet: an unclassified item by description, a route stop
with no ISO code, a ferry with no IMO, boxes nobody has validated so the gross mass is provisional.

Commodity code **`9919 00 00`** covers goods for humanitarian relief — French Customs' guidance for
this traffic, recorded in issue #24 — and is available as `EnsCommodity.HumanitarianAid`. ICS2 requires
at least six digits per goods item.

## The address

**The filing sheet does not carry the consignee's delivery address, and it never will.**

An ENS needs the consignee's name and full address. That address lives in `sensitive.ReceiverDetail`,
which the application identity is `DENY SELECT`'d on, and `receivers:detail` — GroundOfficer alone — is
the only way to read it. Every read is audited in the same transaction
(`ReceiverDetailRepository.ResolveAsync`).

So the sheet names the consignee at organisation and region, sets `consigneeAddressWithheld`, and tells
the filer where to get the rest: `GET /receivers/{ref}/detail`. The filer is a Ground Officer, so they
already hold it. The address goes from their screen into the portal and never through a Freedom queue,
blob or payload — which is what keeps the redaction structural rather than a rule somebody has to
remember (`docs/domain/key-concepts.md` § Data Sensitivity).

## Recording what comes back

```
PUT /manifests/{id}/ens        { "mrn": "25FR17551780961AT5",
                                 "acceptedAt": "2026-08-24T09:30:00+00:00",
                                 "filedBy": "groundofficer",
                                 "filingReference": "STP-2026-0001" }
GET /manifests/{id}/ens
DELETE /manifests/{id}/ens     -- withdraw, because it was invalidated in ICS2 and will be refiled
```

`manifests:declare` — Administrator and Dispatcher, deliberately **not** GroundOfficer, so the filer
passes the MRN back rather than reaching into the manifest slice. An MRN is eighteen characters: two
digits of year, the ISO alpha-2 code of the declaring country, thirteen characters of reference and a
check character, all upper case. Freedom checks that shape and not the check character — refusing an
MRN ICS2 has already issued would strand a convoy over a bug of ours.

It is **write-once**. The envelope names it, so replacing one silently would leave French customs
pairing a crossing against a formality Freedom no longer believes in. To correct one, withdraw it and
record the refiled MRN; the withdrawn one is kept under `ens/{manifestId}/`.

**Approval will not proceed without it.** `POST /manifests/{id}/approve` answers 409 and freezes
nothing.

## Non-amendable fields, and why that matters here

These cannot be amended once the declaration is filed — a mistake means invalidating it and lodging a
new one:

- mode of transport (so: the crossing)
- the declarant and any representative
- the customs office of first entry
- the carrier identification number
- transport document references
- the goods item number
- and, for a ferry, the **vessel IMO**, which may never be modified once entered

That is why `CrossingMode`, `VesselImo` and the goods item ordering are all settled before filing rather
than after, and why `DELETE /manifests/{id}/ens` exists at all.

## What is still missing on our side

| Gap | Consequence |
| --- | --- |
| **The EU EORI question above.** | Until it is answered, no declaration can be filed by anyone, in any environment. |
| **No system-to-system submission.** ICS2's STI speaks eDelivery AS4; Freedom exposes no inbound endpoint and runs nothing always-on. | Filing is manual. At roughly one convoy a month and a handful of vehicles, that is hours, not days — but it is a person's hours. |
| **No amendment or invalidation call.** Withdrawal is recorded in Freedom; the invalidation itself happens in the portal. | The two can drift. Freedom's record says what was filed, not what ICS2 currently holds. |
| **No referral, control or do-not-load notifications.** These come back through the STI. | A dispatcher learns of a control at the terminal. |
| **The filing type code is unconfirmed.** Secondary sources disagree between F50 and F40 for a complete road ENS. | The sheet gives the mode-of-transport code, which is unambiguous, and leaves the dataset to the portal's own choice. Confirm against the ICS2 Functional Specifications before asserting one. |
| **No ENS MRN on the GMR.** GVMS wants it in `sAndSMasterRefNum`, which hangs off a declaration container whose required primary identifier Freedom does not hold. | HMRC is not told which S&S declaration the load travels under. See `docs/gotchas-and-open-questions.md` §5b. |

## Rehearsing it locally

There is no ICS2 stub, because there is nothing to call. The whole path is Freedom's own:

```
cd iac/local && docker compose up -d --wait
cd ../tofu   && tofu apply                      # creates the ens container
cd ../local  && docker compose up -d --wait app edge customs-worker
```

Then, with a Dispatcher token (`operator` / `password` — see `docs/local-authentication.md`):

1. `GET /manifests/{id}/ens/filing-sheet` — read it, and check `missing` is empty.
2. `POST /manifests/{id}/approve` **before** recording anything → 409, and the manifest is untouched.
3. `PUT /manifests/{id}/ens` with a well-formed MRN → 201. Repeat it → 409.
4. `POST /manifests/{id}/approve` → 204, and `GET /manifests/{id}/elo` shows one declaration: the MRN,
   not a placeholder.
5. `DELETE /manifests/{id}/ens` on an unfrozen manifest, then `PUT` again → the withdrawn one is still
   in the `ens` container under `{manifestId}/`.

The refusal path from the French side is still rehearsed by
`iac/local/wiremock/mappings/elo-create-envelope-rejected.json`, but reaching it now means putting a
message on `elo-envelopes` by hand — the API refuses a manifest with no declaration and the validator
refuses one that is not shaped like an MRN. See `iac/README.md`.

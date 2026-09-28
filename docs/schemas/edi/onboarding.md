# Becoming an ELO EDI operator with French Customs

How Ukrainian Action gets from "the code is written" to "a lorry crosses at Calais". None of this is
a software task: it is an authorisation, a signed agreement and a certification run with the
*Direction générale des douanes et droits indirects* (DGDDI), and the lead time is measured in weeks
rather than days.

Sources: the service contract `ELO_CDS_EDI_EN_v1.2_1.pdf` (§2.2.1, §2.2.4, §2.2.5, §3, §4) and
`EDI ELO Certification Process EN.pdf`, both in this folder, plus
[douane.gouv.fr — échange de données informatisé (EDI/API)](https://www.douane.gouv.fr/fiche/echange-de-donnees-informatise-edi-api).

> **Read `docs/domain/key-concepts.md` § ELO first** if you are not sure what an envelope is. In
> short: it is not a goods declaration. It is a barcode that pairs customs formalities other systems
> issued with one physical crossing, and it is required per transport unit at the French Smart
> Border.

---

## What has to be true before a real envelope can be created

Five things, roughly in order. Only the last is ours.

| # | What | Who | Blocking? |
| --- | --- | --- | --- |
| 1 | An EORI number for Ukrainian Action | Already held (`Customs:HaulierEori`) | — |
| 2 | A `douane.gouv` account with the **Correspondant entreprise** right | An administrator at the charity | Yes |
| 3 | A signed **contrat d'utilisation** (user agreement) for EDI/API access | The charity and DGDDI | Yes |
| 4 | Authorisation for the **`API_BREXIT_ELO`** service, and a technical API account | DGDDI, on request | Yes |
| 5 | A passed **certification run** against the MOA environment | Us, with DGDDI | Yes |

§2.2.5.1 of the service contract is blunt about the end state: *"Only service providers with EDI
licences and certified by french customs are able to exchange information electronically."* There is
no sandbox you can self-serve into.

---

## 1. The Correspondant entreprise right

API accounts are administered through the **Correspondant entreprise** teleservice on
`douane.gouv.fr`. Somebody at Ukrainian Action needs that right before anything else can be
requested, because it is the account that later holds the API technical account, its credentials and
its callback URL (§2.2.1).

This is an administrative step on the DGDDI portal, not something Freedom can automate.

## 2. The user agreement and service authorisation

§2.2.4: *"The operator must have EDI access to Brexit IS and hold an access agreement signed with the
DGDDI authorising it to use the dedicated API_BREXIT_ELO webservices."*

Write to **`contact-ssi-brexit@douane.finances.gouv.fr`**, naming:

- the service: **`API_BREXIT_ELO`**
- the **EU EORI** number, or the **INSEE (SIRET)** number for a non-EU operator
- the **software used** — "Ukrainian Action Freedom, a bespoke .NET application" is the honest answer
- the **`douane.gouv` account of the Correspondant entreprise** from step 1

The reply carries the *contrat d'utilisation* to sign and, once returned, the authorisation for the
service.

## 3. The API technical account

Created in the Correspondant entreprise space. It holds:

- the **OAuth2 client credentials** the worker authenticates with
  (`Elo:ClientId` / `Elo:ClientSecret` / `Elo:Username` / `Elo:Password`)
- the **operator callback URL** (`URL_OPERATEUR_EDI`) that French customs would push
  `ENV_NOT01` passage notifications to

> **We do not register a callback URL.** Freedom exposes no inbound endpoint, by design
> (`docs/recommendations.md` §4.1): a permanently reachable public URL, a shared secret to rotate,
> callback authentication and replay protection are all things not to get wrong, defended by a
> free-tier WAF. Pairing, embarkation and disembarkation are readable by polling
> `POST /enveloppe/recuperer` instead, which returns `statut`, `dateAppairage`, `dateEmbarquement`
> and `dateDebarquement`. That polling is not built yet — see *What is still missing* below.
>
> The service contract notes that automating the initial configuration and update of
> `URL_OPERATEUR_EDI` is *"being defined"* and that a manual process applies meanwhile, so declining
> to register one also avoids a manual step that would need repeating.

## 4. Certification

`EDI ELO Certification Process EN.pdf` routes certification through
**`certification-edi@douane.finances.gouv.fr`**.

Certification runs against the MOA environment. The URLs differ only in host:

| Environment | Base URL |
| --- | --- |
| Certification / test | `https://api-moa.douane.gouv.fr/sibrexit/` |
| Production | `https://api.douane.gouv.fr/sibrexit/` |

§2.2.1 adds that *"The URL will be specified at the time of certification testing"*, so treat both as
provisional until DGDDI confirms them. Set whichever applies as `Elo:BaseUrl`; the worker refuses to
start without it, because the published OpenAPI document declares no `servers:` entry and so there is
no sensible default to bake in.

## 5. Authentication

OAuth2 **Resource Owner Password Credentials** against a customs `/oauth2/token` endpoint
(§2.2.5.1). Two details that are easy to get wrong:

- **Reuse the token for its whole lifetime.** The contract asks for this explicitly, *"so as not to
  unnecessarily saturate the authentication server"*. `EloTokenProvider` caches until 30 seconds
  before expiry and is registered as a singleton for exactly this reason.
- **The token is a per-call method argument, not a pipeline concern.** The ELO OpenAPI document
  declares no security scheme at all, so `Authorization` is an ordinary required header parameter
  alongside `messageCode`, `functionalId`, `messageId` and `correlationId`. There is no
  `AddHttpMessageHandler` to chain, unlike the HMRC SDKs.

Transport is **TLS 1.x, one-way**, using the French customs certificate. Nothing client-side to
install.

---

## What Ukrainian Action still cannot do

**An envelope references customs declarations; it does not describe goods.** Freedom does not yet
hold a single real declaration identifier, and cannot obtain one, so a submission to real French
customs would be refused today.

What the rules require for our traffic:

- Issue #24 records French Customs' guidance for this case: *"For emergency humanitarian aid destined
  for Ukraine, select TIR/ATA without transport contract."* That is
  `EloCrossingProfile.HumanitarianAidToUkraine` in the domain.
- Under cross-functional rule **ENV_CTR_RG08**, a loaded lorry arriving in France under TIR/ATA needs
  **at least one ENS** — a safety-and-security entry summary declaration. Without TIR/ATA it would
  need an ENS *and* a customs-clearance formality (an import or transit MRN), so the TIR/ATA ruling
  halves the paperwork.
- An ENS comes from **ICS2**. A transit MRN, if one is ever used, comes from **DELTA-T / NCTS**,
  which Freedom does not integrate with — and under TIR/ATA it does not need to.

**This gap is closed.** The envelope now carries the ICS2 ENS MRN recorded against the manifest, and
`Elo:PlaceholderDeclarationIdentifier` is gone. Approving a manifest is refused outright if no MRN has
been recorded, before anything is frozen. Freedom does not *submit* the ENS — a Ground Officer files it
in the EU Customs Trader Portal and a Dispatcher records the MRN — so read
`docs/schemas/ics2/onboarding.md` next: it carries the one prerequisite that still blocks a real
crossing, which is an EU-issued EORI for a UK-established charity.

Also from issue #24: commodity code **`9919 00 00`** applies to humanitarian aid. It belongs on the
underlying declaration rather than on the envelope, which is why it appeared with ICS2 and not before —
it now lives as `EnsCommodity.HumanitarianAid` and on `dbo.BoxItem.CommodityCode`, and the filing sheet
reports any item without one.

---

## What is still missing on our side

| Gap | Consequence |
| --- | --- |
| **ICS2 integration for the ENS** | A real submission is refused. This is the one blocker. |
| **Status polling** (`POST /enveloppe/recuperer`) | Freedom never learns that an envelope was paired, embarked or disembarked; `statut` stays `FERMEE`. |
| **Envelope modification** (`POST /enveloppe/modifier`) | An envelope may be amended while `FERMEE` and unpaired; we cannot add or remove a declaration after creation. |
| **`ENV_NOT01` consumption** | Deliberate — see the callback note above. The DTO is generated for a future caller. |

---

## Operating limits worth knowing

From §2.2.3 and §4:

- **7 days a week, 24 hours a day, 99% availability.** Maintenance is announced at least 3 days
  ahead.
- **Under 10 seconds** per exchange, and *"It is advisable not to group more than 200 declarations in
  the same envelope"* — beyond that the response time is not guaranteed. Freedom sends one envelope
  per vehicle with one declaration, so this is not near.
- **"No data loss allowed"**, except for extended downtime on the operator's side. This is the
  requirement the queue exists to meet: an envelope request survives a worker restart, a French
  customs outage and a redeploy, and is only deleted once the envelope has been created and stored.
- Errors are `200` success, `400` functional, `401`/`403` authentication, `500` technical. Functional
  refusals carry a `FONC-ERR-00x` code and a free-text `libelleErreur`.

> **The error label quotes the declaration it objected to, and is never logged.** Only the HTTP
> status and the `FONC-ERR-00x` code reach the logs — see `EloEnvelopeProcessor`. The same rule
> applies to HMRC responses and for the same reason: logs are retained, and a customs response body
> can echo a plate, an EORI or a consignment.

---

## Rehearsing it locally

The local simulation runs the whole path against a WireMock stub, with no DGDDI involvement:

```
cd iac/local && docker compose up -d --wait
cd ../tofu   && tofu apply                      # creates elo-envelopes + its poison queue
cd ../local  && docker compose up -d --wait app edge customs-worker
```

Record an MRN with `PUT /manifests/{id}/ens`, approve the manifest, then
`GET /manifests/{id}/elo` and `GET /manifests/{id}/elo/document`. Approving without the MRN answers
409 — that is the ENV_CTR_RG08 guard, moved ahead of the freeze. The refusal path from French customs
is rehearsed by putting a message on `elo-envelopes` by hand with `"declarationIdentifiers":
["REFUSE-ME"]`, which the `elo-create-envelope-rejected.json` mapping answers with a real
`FONC-ERR-004` body; see `iac/README.md`.

**WireMock loads its mappings at boot**, so `docker compose restart wiremock` after editing one.

The BDD feature `BoxDelivery.feature` walks a box from packed to delivered through all of it.

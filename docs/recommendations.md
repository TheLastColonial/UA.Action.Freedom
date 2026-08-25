# Architecture Recommendations — Hosting Freedom on Azure

Companion to [`docs/c4/2-containers.puml`](c4/2-containers.puml). Records the reasoning behind the container
design, the cost model it is built to satisfy, and the security posture that follows from it.

**Operating constraints this design was written against:**

| Constraint | Value |
| --- | --- |
| Budget | Permanent Azure free allowances only. No trial credits, no introductory offers. |
| Load shape | Convoys run roughly **once a month**. Long quiet periods, then a burst of a few days. |
| Identity | Greenfield. No existing Entra tenant or Microsoft 365 accounts. |
| App shape | Web UI and API ship as **one deployable**. |
| Region | UK South (UK data residency for volunteer and donor personal data). |

> **On the figures below.** Azure allowances and prices change. Every number here is indicative and should be
> re-checked against the [Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/) before you
> commit. The *shape* of the argument — what is free forever, what carries a standing charge — is the durable part.

---

## 1. What changed in the container diagram, and why

| Was | Now | Reason |
| --- | --- | --- |
| Azure Front Door | **Cloudflare Free** at the edge | Front Door Standard carries a fixed monthly charge (~$35/mo) whether or not a convoy is running. Cloudflare's free tier covers DNS, TLS, CDN, DDoS protection and managed WAF rules at £0. This is the single largest saving. |
| Separate Web App + API App Service | **One Azure Container App** | Halves compute, removes an internal hop, and removes a whole service-to-service auth boundary that would otherwise need securing. Split later if a mobile or third-party client appears. |
| App Service (always-on plan) | **Container Apps Consumption** | App Service has no scale-to-zero on any tier that is free forever. Container Apps Consumption scales 0..N and has a monthly free grant. Matches the once-a-month burst exactly. |
| Three separate Blob Storage containers (manifest / GMR / ELO) | **One storage account, three prefixes** | Taken literally the old diagram implies three accounts to secure, monitor and pay for. One account with `manifests/`, `gmr/`, `elo/` prefixes is cheaper and has one access policy to get right. |
| `gmr_function --> api : sends status updates` | **Worker writes GMR status to the database** | The worker calling back into the public API meant a second authenticated ingress path and a circular dependency. The worker owns GMR state; it writes it where the app reads it. |
| HMRC Push notifications (inbound webhook) | **Timer-triggered pull** | See [§4.1](#41-pull-from-hmrc-do-not-expose-a-webhook). Removes a public endpoint entirely. |
| No registry shown | **ghcr.io via GitHub Actions** | Azure Container Registry has no free-forever tier (~$5/mo for Basic). GitHub Container Registry is free for the repository. |
| No identity provider detail | **Microsoft Entra External ID** | Free to 50,000 monthly active users, supports self-service sign-up for volunteers, and does not require every volunteer to hold a Microsoft 365 licence. |
| No CI/CD, secrets or telemetry | **GitHub Actions, Key Vault, Application Insights** | These exist in any real deployment; leaving them off the diagram hides real security and cost decisions. |
| Person-to-person and person-to-third-party edges duplicated from level 1 | Removed | A container diagram's job is to show containers and who talks to *them*. `guard → driver`, `dispatcher → ferry` and `donor → website` remain on `1-system-context.puml`, which is where they belong. |

**Two additions are proposals, not decisions** — flagged here so they are easy to remove:

- `website → app` — the public site posting driver applications and donation declarations into Freedom, rather
  than the current arrangement where those arrive by some out-of-band route. See [§5.1](#51-close-the-loop-from-the-public-website).
- `guard → edge` — QR-code manifest verification for border guards. See [§4.5](#45-give-border-guards-a-scoped-read-only-view).

---

## 2. The cost model

### 2.1 Where the money would go

| Component | Service | Free allowance (per month) | Expected cost |
| --- | --- | --- | --- |
| Edge / WAF / CDN | Cloudflare Free | Unlimited requests, managed rules | **£0** |
| Freedom Application | Container Apps (Consumption) | 180,000 vCPU-s · 360,000 GiB-s · 2M requests | **£0** within budget — see §2.2 |
| Customs Worker | Azure Functions (Consumption) | 1M executions · 400,000 GB-s | **£0** |
| Freedom Database | Azure SQL free offer (serverless) | 100,000 vCore-s · 32 GB data | **£0** within budget — see §2.3 |
| Document Store | Blob Storage (Cool) | none | ~£0.10 at a few GB |
| Customs Work Queue | Queue Storage | none | pennies |
| Secret Store | Key Vault (Standard) | none, but **no standing charge** | ~£0.05 with caching |
| Telemetry | Application Insights / Log Analytics | 5 GB ingestion | **£0** with sampling + daily cap |
| Identity | Entra External ID | 50,000 MAU | **£0** |
| Public Website | Static Web Apps (Free) | 100 GB bandwidth | **£0** |
| Source, CI/CD, registry | GitHub Actions + ghcr.io | free for the repo | **£0** |
| Notifications | ACS Email | none | ~£0.10 at a few hundred emails |

**Realistic floor: under £1–2/month**, dominated by storage and per-operation charges rather than compute.
Against the original design (Front Door + two App Service plans + ACR) this is roughly **£30–35/month saved,
about £400/year** — real money for a charity.

### 2.2 The Container Apps budget, in hours

The free grant is easier to reason about converted into replica-hours. At the smallest useful sizing of
**0.25 vCPU / 0.5 GiB**:

```
180,000 vCPU-s ÷ 0.25 vCPU = 720,000 s = 200 hours
360,000 GiB-s ÷ 0.5  GiB   = 720,000 s = 200 hours
```

**You get ~200 replica-hours per month free — about 8 days of one always-warm replica.**

That maps onto the convoy rhythm almost perfectly:

- **Default `minReplicas: 0`.** Between convoys the app costs nothing and the first request after idle pays a
  cold start of a few seconds. Acceptable for a system nobody is using that week.
- **Scale to `minReplicas: 1` for the convoy window** (say the five days spanning manifest build, loading and
  departure) — roughly 120 of the 200 free hours, leaving ~80 hours of headroom for burst replicas.
- Automate the switch with a scheduled GitHub Actions job or an `az containerapp update` triggered from the
  convoy's planned start date, so nobody has to remember.

Set `maxReplicas` to a small number (3–5). It caps both a traffic spike and the bill.

### 2.3 The Azure SQL free offer has a sharp edge — read this one

The free offer gives **100,000 vCore-seconds/month**. Serverless has a floor of 0.5 vCores, so:

```
100,000 vCore-s ÷ 0.5 vCores = 200,000 s ≈ 55 hours of active database compute per month
```

That is ~1.8 hours a day. Workable for monthly convoys — but there is a trap:

> **The minimum auto-pause delay is 60 minutes.** Every time the database wakes it stays billable for at
> least an hour after the last query. Ten scattered five-minute check-ins across a day cost ten hours of
> budget, not fifty minutes.

Mitigations, in order of preference:

1. **Set the free-offer exhaustion behaviour to auto-pause, not billing.** The offer lets you choose what
   happens when the monthly allowance runs out. Choose "pause the database" so an unexpected burst can never
   produce an invoice. Accept that the service is then unavailable until the month rolls over, and alert on it.
2. **Set auto-pause delay to the 60-minute minimum** and monitor `vCore-seconds` consumed weekly.
3. **Batch background work.** Have the Customs Worker do its HMRC polling in scheduled passes rather than
   continuously, so it wakes the database on a predictable rhythm instead of keeping it warm all day.
4. **If the ceiling bites, reconsider the store.** Azure Cosmos DB's free tier (1,000 RU/s and 25 GB, free
   forever) has no compute-hours ceiling and never pauses — a better fit for a bursty free-tier workload, at
   the cost of losing relational joins that this domain (manifests → boxes → items, vehicles → convoys) genuinely
   wants. Start on Azure SQL, instrument it, and only move if the data says so. Do not pre-optimise for this.

### 2.4 Cost guardrails are a security control

An attacker cannot easily take Freedom down, but they can run up a bill — "denial of wallet". Treat these as
security controls, not housekeeping:

- Azure Budget on the subscription with alerts at £5 / £10 / £25.
- Log Analytics **daily ingestion cap** (e.g. 200 MB/day) so a logging loop cannot burn the 5 GB allowance.
- Application Insights **sampling** enabled from day one.
- `maxReplicas` capped on both the Container App and the Function App.
- Azure SQL free-offer exhaustion set to auto-pause (§2.3).
- Cloudflare rate limiting on the free tier plus in-app rate limiting (§4.6).

### 2.5 If the nonprofit grant lands

[Azure for Nonprofits](https://www.microsoft.com/nonprofits/azure) grants a sponsored Azure credit (recently
~$2,000/year — confirm the current amount) plus discounted rates, and Microsoft 365 Business Premium is granted
free for up to 10 users. **Apply for it regardless** — the application costs nothing and the design does not have
to change to take advantage.

Spend the credit in this order:

1. **`minReplicas: 1` all month** (~£25–35/mo). Removes cold starts entirely. Biggest quality-of-life gain per pound.
2. **Lift the database ceiling** — move off the free offer to serverless without the cap, or a small provisioned
   tier. Removes the §2.3 trap and the auto-pause tail entirely.
3. **Conditional Access via the M365 grant, not credit.** M365 Business Premium includes Entra ID P1, which brings
   Conditional Access, named locations and risk policies. Ten free seats covers the internal roles
   (Administrator, Dispatcher, Purchaser, Loader, Ground Officer) — so put *staff* on the granted Entra ID
   workforce tenant and leave *volunteers* on External ID. This buys the strongest security control available
   at £0.
4. **Azure Container Registry Basic** (~£4/mo) if you want image scanning and private images inside Azure.
5. **Microsoft Defender for Cloud** on the storage account and SQL database.

**Do not** buy Azure Front Door Premium for its WAF (~$330/mo). It is out of proportion to this system even with
a grant. Cloudflare stays.

---

## 3. Notes on the runtime choices

### 3.1 Do not use Blazor Server with scale-to-zero

Blazor Server holds a stateful SignalR circuit per user. Scaling to zero, or scaling out without sticky sessions,
drops those circuits and users see reconnect banners mid-form — exactly during the loading-day burst when the app
is under most use. Prefer **Razor Pages / MVC with progressive enhancement**, or **Blazor WebAssembly** served as
static files with the API behind it. If Blazor Server is wanted anyway, `minReplicas: 1` becomes mandatory and
§2.2's hour budget must be re-planned.

### 3.2 Persist ASP.NET Core data protection keys — this will bite you

Container Apps replicas are ephemeral. By default ASP.NET Core generates its data-protection key ring in the
container filesystem, so **auth cookies and antiforgery tokens break on every restart and every scale-out event**.
With scale-to-zero that means users are silently logged out after every idle period.

Persist the key ring to Blob Storage and encrypt it with a Key Vault key:

```csharp
builder.Services.AddDataProtection()
    .PersistKeysToAzureBlobStorage(blobUri, credential)
    .ProtectKeysWithAzureKeyVault(keyUri, credential);
```

This is the single most common way a Container Apps deployment of an ASP.NET Core app appears to "randomly log
people out". Fix it before the first user sees it.

### 3.3 Cold starts are acceptable here — say so out loud

At `minReplicas: 0` the first request after an idle period takes a few seconds, and the database may take longer
still if it has auto-paused. Set expectations in the UI: a loading state on first navigation, and no aggressive
client timeouts. This is a deliberate trade of latency for cost, and it is the right trade for a system used
intensively a few days a month.

---

## 4. Security

The free tier removes some paid safety nets (no Azure WAF, no Conditional Access, no Defender). The design
compensates by **reducing attack surface** rather than by adding controls it cannot afford.

### 4.1 Pull from HMRC; do not expose a webhook

The Push Pull Notifications API supports both push (HMRC calls a URL you host) and pull (you poll for
notifications). **Choose pull.**

- Push requires a permanently reachable public endpoint, a shared secret to rotate, callback authentication to get
  right, and replay protection — all defended by a free-tier WAF.
- Pull requires none of that. The Customs Worker holds outbound-only credentials, and there is nothing to attack.
- The cost is latency: outcomes arrive in minutes rather than seconds. GMR turnaround is not a seconds-level
  business process, so this costs nothing operationally.
- It also suits the burst: poll on a timer **only while a GMR is in flight**, and fall idle otherwise, which keeps
  the Function inside its free grant and stops it waking the database (§2.3).

### 4.2 Managed identity everywhere; no connection strings

Target **zero secrets in configuration**:

- Azure SQL with **Entra-only authentication** — disable SQL authentication on the server so no password exists to leak.
- Blob and Queue Storage accessed by managed identity. **Disable shared key authorisation** on the storage account,
  which turns off account keys entirely and forces every access through Entra RBAC.
- Key Vault accessed by managed identity, holding only what genuinely cannot be an identity: the HMRC OAuth client
  credentials.
- **GitHub Actions authenticates by OIDC federated credential**, not a stored service principal secret. Nothing
  long-lived sits in GitHub.

Use a separate app registration, vault and storage account per environment. Never let a non-production deployment
hold production HMRC credentials.

### 4.3 Documents are private; access is time-boxed

- Public blob access **off** at the account level.
- Serve manifests, ELOs and GMRs through **short-lived user-delegation SAS** (10–15 minutes), generated per request
  after the app has authorised the user. User-delegation SAS is signed with an Entra key, so it can be revoked and
  is not tied to an account key.
- Enable **soft delete and blob versioning**. Both are cheap at this data volume and they cover the realistic
  failure mode — someone overwrites the wrong manifest the night before departure.
- Never put a document URL in an email. Email a link to an authenticated page that mints the SAS.

### 4.4 Treat Ukrainian delivery detail as the most sensitive data in the system

`key-concepts.md` already calls for "segregation of the delivery logistics from the sensitive details of a
delivery". That is a security requirement, and it is more important than anything else on this list: a manifest
listing precise delivery addresses is a targeting document, and it crosses several borders in a vehicle where it
may be inspected, photographed or seized.

Concrete measures:

1. **Separate the data.** Hold `Receiver` address detail in its own schema (e.g. `sensitive.Receiver`) with its
   own database role, granted only to Ground Officer. The rest of the application joins on an opaque receiver
   reference and never selects the address.
2. **Redact what travels.** The printed manifest and the border-guard view show cargo, weights and a
   region-level destination — never the street address or contact name. Full detail is released to the driver at
   the point of delivery, not at load time.
3. **Audit every read.** Log who resolved a receiver's full address and when, to the telemetry container. This is
   the one place where an audit trail matters more than the data itself.
4. **Consider Always Encrypted** on the address and contact columns. It is available on Azure SQL at no extra
   cost and keeps plaintext out of the database engine entirely. Weigh it against the operational complexity of
   key management before committing.
5. **Set a retention policy.** Delete receiver detail a defined period after delivery is confirmed. Data you no
   longer hold cannot be disclosed.

### 4.5 Give border guards a scoped, read-only view

Today a border guard verifies a convoy by talking to the driver and reading paper. A guard cannot be given an
account, but a QR code on the printed manifest can link to a verification page that is:

- **Scoped to one manifest**, via a signed token embedded in the QR code — not an enumerable identifier.
- **Short-lived**, valid for the convoy window only.
- **Read-only and redacted**, showing vehicle, weights, box counts and contents categories — and, per §4.4, no
  receiver address or contact.
- **Rate-limited and logged**, so scans are visible and abuse is detectable.

This is a proposal, not a decision. It makes verification faster and reduces the pressure to print sensitive
detail onto the manifest, but it needs a conversation with someone who has actually stood at the border.

### 4.6 Compensating controls for the missing WAF

- **Cloudflare Free** gives managed rules, DDoS protection and basic rate limiting at the edge.
- **In-app rate limiting** using the built-in ASP.NET Core rate limiter, applied hardest to anonymous endpoints:
  the manifest verification page (§4.5) and the public website's application form (§5.1).
- **Lock the origin to Cloudflare.** Otherwise an attacker who finds the Container Apps default hostname bypasses
  the edge entirely. Restrict Container Apps ingress to Cloudflare's published IP ranges, or use a Cloudflare
  Tunnel, and verify it — this is the step most often skipped.
- **Standard hardening**: HSTS, a strict Content-Security-Policy, secure/`SameSite` cookies, and antiforgery
  tokens on every form.

### 4.7 Identity and roles

- **Entra External ID** as the identity provider for everyone, with email OTP sign-in so volunteers need no
  Microsoft account. Email-based MFA is available without a per-user licence; SMS is billed per message, so
  prefer authenticator or email factors.
- **Require MFA in the sign-up/sign-in user flow** for the internal roles. Without Entra ID P1 there is no
  Conditional Access, so MFA has to be enforced in the flow itself. (If the M365 nonprofit grant lands, move
  staff to the workforce tenant and use Conditional Access instead — §2.5.)
- **App roles, not group membership.** Group-based assignment to an enterprise application requires a P1 licence;
  app roles do not. Define one app role per role in `key-concepts.md` and enforce with authorisation policies.
- **Least privilege by role.** A Loader confirms box contents and weights; they do not need to see convoy routes
  or receiver detail. A Purchaser records vehicles; they do not need manifests. Model this explicitly — the roles
  are already well-defined in the domain, so there is no excuse for a single "staff" role.
- **Offboarding matters.** Volunteers turn over. Ensure the Administrator has one obvious place to revoke access,
  and review dormant accounts each convoy cycle.

### 4.8 Volunteer personal data

`Person` holds name, date of birth, phone number and join date. That is personal data under UK GDPR:

- Keep it in **UK South**. Do not let a telemetry pipeline or a backup copy leave the UK.
- Keep it **out of logs**. Log identifiers, never names, phone numbers or dates of birth. Application Insights
  retains data for 31 days on the free tier — assume anything logged is stored.
- Have a **retention and deletion answer** before launch, not after someone asks for their data to be erased.
- Ask why date of birth is needed. If it is for driver eligibility or insurance, store the derived fact
  ("eligible to drive: yes/no") and consider not storing the date at all.

---

## 5. Open questions and proposals

### 5.1 Close the loop from the public website

The context diagram shows donors requesting donation sheets and drivers applying via the public website, but
nothing connects those to Freedom — so today they presumably arrive by email and get re-keyed. Proposal: the
Static Web App posts applications and donation declarations to a rate-limited, anonymous endpoint on the Freedom
Application, creating a `Person` in a pending state for an Administrator to approve.

**Needs a decision** — it adds the only anonymous write path into the system, so it needs rate limiting, spam
protection (Cloudflare Turnstile is free) and an explicit approval step before a record becomes real.

### 5.2 Questions worth answering before building

1. **Do dispatchers and loaders need the system to work offline or on poor connections?** Loading happens in a
   warehouse; border handover happens in a lorry park. If offline matters, that changes the client architecture
   far more than any hosting decision here, and it should be settled early.
2. **Who is accountable if a manifest is wrong at the border?** That determines how strictly the system must
   prevent a manifest changing after the GMR is submitted, and whether manifests need to be immutable and
   versioned once confirmed.
3. **Does the ELO need to be generated by Freedom, or is it produced elsewhere and attached?** The current design
   assumes generation; the process diagram is ambiguous.
4. **How many convoys run concurrently?** The design assumes one convoy cycle at a time. Overlapping convoys
   would change the database budget in §2.3 materially.
5. **What is the recovery expectation?** If the free-tier database auto-pauses mid-convoy (§2.3), what should
   happen — degrade to read-only from cached documents, or accept an outage? This is a business decision.

### 5.3 Reconcile `ManifestStatus` with `manifest-status.puml`

Out of scope for this document but worth flagging: the code defines six manifest states while
`docs/manifest-status.puml` documents ten. This design assumes the `.puml` is the target. Confirm before building
the workflow, because the status model determines where the GMR submission is triggered from.

---

## 6. Suggested order of work

1. Provision the subscription with budget alerts and the Log Analytics daily cap **first** (§2.4). Guardrails
   before workloads.
2. Entra External ID tenant, app registration, and the app roles from `key-concepts.md` (§4.7).
3. Container App + Azure SQL free offer, wired with managed identity and Entra-only auth (§4.2), with data
   protection keys persisted from the very first deploy (§3.2).
4. GitHub Actions deployment using OIDC federation (§4.2).
5. Storage account with shared-key auth disabled, soft delete on, and SAS-based document serving (§4.3).
6. The sensitive-data split for `Receiver` (§4.4) — do this while the schema is still small and cheap to change.
7. Cloudflare in front, then lock the Container Apps origin to it and **verify the bypass is closed** (§4.6).
8. Customs Worker with pull-based HMRC polling (§4.1).
9. Convoy-window warm-up automation (§2.2).

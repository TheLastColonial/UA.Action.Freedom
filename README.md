# UA.Action.Freedom

Automation to support Ukrainian Action, a charity that runs supply convoys (donated vehicles + cargo) from the UK to Ukraine. This system models the complete lifecycle of preparing a convoy: sourcing vehicles, packing boxes of items, crewing each vehicle for both legs of the journey, building a manifest per vehicle, and tracking convoy routes and status through to delivery.

Built on .NET 10 with ASP.NET Core minimal APIs, Dapper for data access, and OpenTelemetry for observability.

## Getting Started

### Prerequisites

- **.NET 10 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet)
- **Node 24 LTS** — for the operator web UI (`web/`); `nvm use` picks it up from `web/.nvmrc`
- **Docker Desktop** — for local infrastructure simulation
- **PowerShell** or **Bash** — for build/test scripts
- **OpenTofu** (optional) — for provisioning local resources (`iac/tofu/`)

### Parallel work (optional)

Several developers or agents can each have an isolated git worktree *and* full local stack (own
ports, containers, database, Keycloak) plus hot reload; see
[`docs/parallel-agents.md`](docs/parallel-agents.md). Needs git 2.31+ and PowerShell 7 (`pwsh`);
Pester 5 to run `scripts/agent/tests`; the Dev Containers extension is optional.

### Installation

Clone the repository:
```bash
git clone https://github.com/your-org/UA.Action.Freedom.git
cd UA.Action.Freedom
```

Restore dependencies:
```bash
dotnet restore UA.Action.Freedom.slnx
```

## Project Structure

```
src/
├── UA.Action.Freedom.Domain/           # Plain C# domain model (no framework deps)
├── UA.Action.Freedom.Application/      # Use cases, CQRS handlers, orchestration
├── UA.Action.Freedom.Data/             # Dapper repositories, SQL persistence
├── UA.Action.Freedom.Api/              # ASP.NET Core minimal API host
├── UA.Action.Freedom.CustomsWorker/    # HMRC GMR submission & outcomes, French ELO envelopes
├── UA.Action.Freedom.ManifestWorker/   # Manifest document rendering
├── UA.Action.Freedom.Telemetry/         # Shared OpenTelemetry wiring, span redaction, queue & worker metrics
├── HMRC.GVMS/                          # HMRC Goods Vehicle Movements SDK
├── HMRC.PushPullNotifications/         # HMRC Push Pull Notifications SDK
└── EDI.ELO/                            # French customs ELO (EDI) SDK

database/
├── UA.Action.Freedom.Database/         # SQL project → dacpac: tables, schemas, roles, grants
├── seed/dev-seed.sql                   # Opt-in fictional dev data (never in the dacpac)
├── Dockerfile                          # db-deploy image: SqlPackage + the dacpac
└── deploy.sh                           # Publish the dacpac, optionally load the seed

tests/
├── UA.Action.Freedom.Tests.Unit/       # Handler & unit logic tests
├── UA.Action.Freedom.Tests.Component/  # In-memory API tests
├── UA.Action.Freedom.Tests.Integration/ # Real database tests
├── UA.Action.Freedom.Tests.BDD/        # Reqnroll feature scenarios
├── HMRC.GVMS.Tests.Unit/
├── HMRC.PushPullNotifications.Tests.Unit/
└── EDI.ELO.Tests.Unit/

web/                                    # React + Vite operator UI (TypeScript, strict)
├── src/                                # App shell, api client, auth, pages per slice
├── e2e/                                # Playwright smokes against the running stack
└── ...                                 # built to web/dist, baked into the API image at /app

iac/
├── local/                              # Docker Compose substrate (all services)
│   └── docker-compose.dev.yml          #   Hot-reload override (dotnet watch, Vite)
└── tofu/                               # OpenTofu provisioning (resources)

scripts/agent/                          # Worktree + isolated stack per agent (agent.ps1, agent.sh, Pester tests)
.devcontainer/                          # Dev container toolchain (experimental)
.claude/skills/agent-worktree/          # Claude skill: the parallel-agent checklist

docs/
├── domain/                             # Business rules (target design)
│   ├── key-concepts.md                 #   Shared vocabulary & domain concepts — start here
│   ├── convoy-operations.md            #   Convoy, crew, accommodation, budget, Convoy Leader, readiness
│   ├── boxes-and-donations.md          #   Boxes, items, donors, Receivers, reporting
│   ├── customs-declarations.md         #   GMR, ENS, ELO and the Ukrainian goods list
│   ├── decisions.md                    #   Every decision, its rationale, and open questions
│   └── ua-customs-requirements.md      #   Research on Ukrainian customs (not authoritative)
├── plans/                              # Implementation plans for ADRs 0004–0017 (one PR each)
├── adr/                                # Architecture decision records
├── sequences/                          # Sequence diagrams of each new flow
├── process/                            # Process (swimlane) diagrams, incl. the end-to-end overview
├── states/                             # State diagrams: box, convoy, truck-list entry, declaration, Receiver
├── use-cases/                          # Use case diagrams: who can do what
├── model/domain-model.puml             # Class diagram of the target domain
├── timeline/convoy-timeline.puml       # Gantt worked example of the convoy time rules
├── process.puml, manifest-status.puml  # Manifest process and states (target design)
├── parallel-agents.md                  # One worktree + stack per agent, ports, hot reload
├── local-authentication.md             # Token & role setup guide
├── gotchas-and-open-questions.md       # Debugging & known traps
├── recommendations.md                  # Azure architecture & design decisions
├── schemas/edi/onboarding.md           # Becoming an ELO EDI operator with French Customs
└── c4/                                 # C4 system & container diagrams
```

## Usage

### Build

```bash
dotnet build UA.Action.Freedom.slnx
```

### Run Tests

```bash
# All tests (MTP mode)
dotnet test --solution UA.Action.Freedom.slnx

# Unit tests only
dotnet test --project tests/UA.Action.Freedom.Tests.Unit/UA.Action.Freedom.Tests.Unit.csproj

# A single test
dotnet test --project tests/UA.Action.Freedom.Tests.Unit/UA.Action.Freedom.Tests.Unit.csproj \
  --filter-query "/*/*/ClassName/MethodName"

# Integration tests (requires local SQL)
dotnet test --project tests/UA.Action.Freedom.Tests.Integration/UA.Action.Freedom.Tests.Integration.csproj
```

### Run the API

Start the API on http://localhost:5100:
```bash
dotnet run --project src/UA.Action.Freedom.Api/UA.Action.Freedom.Api.csproj
```

Check health endpoints:
```bash
curl http://localhost:5100/health/live
curl http://localhost:5100/health/ready
```

### Run the web app

The operator UI lives in `web/`. For a fast loop, run the Vite dev server (it proxies API
calls to the edge, so no CORS):

```bash
cd web
npm ci
npm run dev            # http://localhost:5173/app/  (API proxied to http://localhost:8080)
```

Point the proxy elsewhere with `VITE_API_PROXY_TARGET` (e.g. `http://localhost:5100` for a
bare `dotnet run`). The production bundle is built into the API image and served at
`http://localhost:8080/app/` — there is no need to run `vite build` locally.

### Run the web tests

```bash
cd web
npm run test           # Vitest Browser Mode (real Chromium) + Testing Library + MSW
npm run e2e:install    # one-time: download the Playwright browser
npm run e2e            # Playwright smokes — needs the docker stack up; self-skips otherwise
npm run verify         # typecheck + lint + format + test + build (what CI runs)
```

### Local Development Environment

Run the full local infrastructure (SQL, Blob/Queue Storage, Keycloak auth, and WireMock standing
in for both the HMRC APIs and the French customs ELO API):

```bash
# Start Docker containers (db-deploy publishes the database schema and exits)
cd iac/local
cp .env.example .env
docker compose up -d --wait

# Provision resources and database logins via OpenTofu
cd ../tofu
tofu init
tofu apply

# Verify all services are healthy
curl http://localhost:8080/health/ready

# (Optional) Load fictional seed data: depots, volunteers, a convoy, vehicles, boxes, the fixed item categories
cd ../local && docker compose up db-seed
```

**Another stack at the same time?** Do not copy this; create an isolated worktree and stack with
`pwsh scripts/agent/agent.ps1 new <name> -Up` (own ports from a slot, hot reload for the .NET services
and the web UI). See [`docs/parallel-agents.md`](docs/parallel-agents.md).

The `db-seed` service is optional and re-runnable — it guards against seeding an already-populated
database. Integration/BDD suites create their own data and do not invoke it.

`tofu apply` also provisions the public PKCE Keycloak client (`freedom-spa`) the operator UI
signs in with. The UI is then at <http://localhost:8080/app/>; all three seed logins work
through the browser.

**Test logins** (all have password `password`):
- `admin` — Administrator role
- `operator` — Dispatcher, Loader, Mechanic, Purchaser roles
- `groundofficer` — GroundOfficer role (segregated access to delivery addresses)

**After changing code**, rebuild the images before running the BDD or Playwright suites, which drive
the *deployed containers* rather than an in-process host:

```bash
cd iac/local
docker compose build app customs-worker manifest-worker
docker compose up -d --wait app edge customs-worker manifest-worker
```

**After editing a WireMock mapping** (`iac/local/wiremock/mappings/`), restart that container —
mappings are loaded at boot, so an edited stub does nothing until then:

```bash
docker compose restart wiremock
```

### Container images

CI builds and publishes the deployable artifacts — one container image per service, to
GitHub Container Registry (public):

| Image | Contents |
| --- | --- |
| `ghcr.io/thelastcolonial/ua-action-freedom-api` | ASP.NET Core host + the operator SPA (baked in) |
| `ghcr.io/thelastcolonial/ua-action-freedom-customs-worker` | Customs Worker |
| `ghcr.io/thelastcolonial/ua-action-freedom-manifest-worker` | Manifest Worker |

Tags: `<semver>` (e.g. `1.4.0`), `sha-<short>`, and `latest` on `main`. Pushes happen only on
`main` and `workflow_dispatch`; pull-request runs build the images and test them but do not push.
The images are built inside the `acceptance` job so the image that passes the end-to-end suite is
the one that ships. `iac/local/docker-compose.yml` still builds its own `:local` images for the
local environment — unchanged.

The workflow only triggers on changes under `src/`, `tests/`, `database/`, `iac/`, `web/`,
`build/`, the root `UA.Action.Freedom.slnx` / `global.json` / `GitVersion.yml`, or the workflow
file itself. Doc-only commits (`docs/**`, `plans/**`, `*.md`) do not start a build; use
`workflow_dispatch` to force a run.

The database ships separately. `.github/workflows/database.yml` (triggered by `database/**`)
builds the dacpac, publishes it to an empty SQL Server, fails if a second publish would change
anything, and on `main`/`workflow_dispatch` pushes it as an OCI artifact with a provenance
attestation:

| Artifact | Contents |
| --- | --- |
| `ghcr.io/thelastcolonial/ua-action-freedom-database` | `UA.Action.Freedom.Database.dacpac` (`application/vnd.microsoft.sql.dacpac`) |

Fetch it with `oras pull ghcr.io/thelastcolonial/ua-action-freedom-database:<tag>` and deploy with
`sqlpackage /Action:Publish` — see [Database](#database) below.

### API Endpoints

Core resource endpoints:
- `GET|POST /vehicles` — Vehicle inventory (natural key: VIN), including optional cargo capacity (max weight, dimensions) and a value in pounds with its source (`Purchased` for the price paid, `Estimate` for a donated vehicle); writes are Administrator, Purchaser and Mechanic (`vehicles:write`)
  - `PUT /vehicles/{vin}/inspection` — Record the servicing inspection (`Pending`/`Inspecting`/`Passed`/`Failed` + notes) — **Administrator and Mechanic only** (`vehicles:service`). The ordinary `PUT /vehicles/{vin}` cannot change it — nor which convoy the vehicle is on, which only `/convoys/{id}/vehicles/{vin}` changes
- `GET|POST /people` — Volunteers & drivers; `DELETE /people/{id}` **erases** a volunteer (their personal data is deleted; past records show "Former volunteer"), refused with 409 while they are crewing a convoy that has not arrived or a vehicle whose load is not yet finished
  - `PUT /people/{id}/login` — Link the login (token subject) a volunteer signs in with — **Administrator only** (`people:write`); 204, 404, or 409 when the login already belongs to another volunteer. An Administrator may link their own login, so the first link can be made
- `GET /me` — Who the caller is: subject and roles, plus person id and display name once their login is linked. Any authenticated caller, Ground Officer included. **Every write** is signed as the caller's linked volunteer and refused with `403` / `login-not-linked` from a login nobody has linked — there is no body field to forge it with. The only writes an unlinked login may make are `POST /people` and `PUT /people/{id}/login`, which are how a login becomes linked
- `GET|POST /convoys` — Convoy groups with routes
  - `PUT /convoys/{id}/vehicles/{vin}/handover-receiver` — The registered Receiver a vehicle is handed over to in Ukraine (`convoys:write`; 409 not registered, 422 unknown). Shown as `handoverReceiverRef` on the truck list; departure does not require it yet
  - `PUT|GET /convoys/{id}/route` — Ordered stop list
  - `PUT /convoys/{id}/route` answers `200` with the saved points: each has a stable `routePointId`, a `name`, a `kind` (`Stop`, `Overnight`, `Border`, `Hub`) and, for a `Border` point only, an `authority` (`UK`, `EU`, `UA`). Saving **merges** by id: a point sent with its id keeps it, one without is new, one left out is deleted (`422` for an id not on this convoy's route, `409` `route-point-in-use` when something refers to it). `convoys:write`
  - `GET /convoys/{id}/leader` — The Convoy Leader now and the history, newest first (`convoys:read`)
  - `PUT /convoys/{id}/leader` — `{ "personId" }`: nominate the leader, closing the previous assignment. They must be a **Driver crewed on the convoy** (`422`); the sitting leader again is `409`; a leader cannot be taken off the crew until another is nominated (`409`). **Dispatcher or Administrator** (`convoys:lead-assign`)
  - `GET /convoys/{id}/vehicles` — The truck list, withdrawn vehicles included (each entry says which it is)
  - `PUT /convoys/{id}/vehicles/{vin}` — Put a vehicle on the truck list; only one that has **Passed** its inspection, has not been handed over, and is not travelling with another convoy may join (409 otherwise)
  - `DELETE /convoys/{id}/vehicles/{vin}` (`?reason=`) — Before publication this takes the vehicle off the list with its crew and insurance. **Afterwards it is a withdrawal**: the entry, its crew, its insurance and its manifest all stay, because a vehicle that breaks down still has paperwork describing a real load (`convoys:write`)
  - `GET|PUT|DELETE /convoys/{id}/vehicles/{vin}/crew/{personId}` — Vehicle crew: an optional `{ "role": "Driver" | "Passenger" }` body (default Driver); a person takes one seat per convoy (**Dispatcher only** for `PUT`/`DELETE`; `GET` is in `convoys:read`)
  - `POST /convoys/{id}/vehicles/{vin}/manifest` — **Open the manifest for this vehicle on this convoy.** There is no `POST /manifests`: a manifest is the paperwork for a truck-list entry, and `(ConvoyId, Vin)` is a composite foreign key to it (`convoys:write`)
  - `GET|PUT|DELETE /convoys/{id}/vehicles/{vin}/insurance` — The vehicle's insurance for this convoy. It names the drivers it covers (`uncoveredDrivers` lists crew drivers added since); removing a driver keeps it in cover, and a manifest cannot depart without it covering every driver (`convoys:write`)
  - `GET /convoys/{id}/vehicles/{vin}/boxes`, `PUT|DELETE /convoys/{id}/vehicles/{vin}/boxes/{boxId}` — The vehicle's **cargo**: the boxes allocated to its truck-list entry. A box is on at most one vehicle, so `PUT` on a second one *moves* it. Refused `409` for a withdrawn vehicle or one whose manifest has a GMR (until plan 15). Read is `boxes:read`, write is `boxes:write` (Administrator, Dispatcher, Loader)
  - `GET|PUT|DELETE /convoys/{id}/vehicles/{vin}/ferry` — The vehicle's outbound ferry booking (operator, reference, sailing, ticket details, optional cost) — per vehicle, outbound only, because vehicles are handed over rather than driven back (`convoys:write`)
  - `GET /convoys/{id}/readiness` — Advisory readiness: **one driver per vehicle required, two advised**, insurance naming every driver, and a route. Withdrawn vehicles are skipped (`convoys:read`)
  - `POST /convoys/{id}/arrive` — Mark arrived once every vehicle still travelling has a finished manifest; Delivered/Lost vehicles are handed over for good (`convoys:write`)
  - `POST /convoys/{id}/publish-truck-list` — Close the truck list to additions
- `GET|POST /receivers` — Delivery contacts (reference/org/region/**status**). A new receiver is `Pending`; only `Registered` can be a box destination or a vehicle's handover receiver
  - `PUT /receivers/{ref}/status` — Register, suspend or expire a receiver — **Administrator only** (`receivers:register`); narrower than `receivers:write`, so the Ground Officer who records a receiver cannot grant its registration. `GET /receivers/{ref}/usage` (same policy) lists the box and live-convoy ids a change touches, never an address
  - `GET|PUT /receivers/{ref}/detail` — **GroundOfficer only**: delivery address + contact
- `GET|POST /boxes` — Packing containers
  - `GET|POST|DELETE /boxes/{id}/items` — Item inventory. An item names its **category** and may carry a quantity, a value in pounds with its source (`Donor` or `Estimate`), an expiry date and a commodity code of its own. `POST` answers `200` with `{ itemId, warnings }`: a not-carried or already-expired item is accepted and warned about, and a short-dated one warns. Reads carry the category name, `isNotCarried` and `shelfLife` (`Fine`, `Short`, `Expired`)
  - `POST /boxes/{id}/validate` — Lock box weight and optional dimensions. **Refused with `409 box-has-expired-items`** while an item that has already expired is in the box
  - `POST|GET|DELETE /boxes/{id}/qr-code` — Issue / read / revoke the box's QR label (`boxes:write` to issue and revoke, `boxes:read` to read)
  - `GET /boxes/{id}/qr-code/image` (`?format=svg\|png`) — The QR image alone (`boxes:read`)
  - `GET /boxes/{id}/label` — Printable SVG label: QR + box number, no receiver detail (`boxes:read`)
  - `GET /boxes/scan/{token}` — Resolve a scanned token to its box (`boxes:read`)
- `GET /manifests` — The document pack for one vehicle on one convoy: border weight, GMR. Its cargo and ferry booking are on the same truck-list entry. **Created on its convoy** (above), not here
  - `PUT /manifests/{id}` — Notes only. The convoy and the vehicle are the manifest's identity, so there is no field for either
  - `GET /manifests/{id}/crew` — Who is travelling with its vehicle. A **read**: crewing happens once, on the truck-list entry
  - `GET|PUT|DELETE /manifests/{id}/boxes/{boxId}` — Cargo is read through the vehicle's allocations. `PUT`/`DELETE` here answer **410 Gone**: put boxes on the vehicle with `PUT /convoys/{id}/vehicles/{vin}/boxes/{boxId}`
  - `GET /manifests/{id}/elo` — The French logistics envelope for this vehicle: its `jeton`, `numeroDossier` and `statut`. **Read-only** — an envelope is requested by filing the vehicle's ELO declaration, never by a `POST` here — and `404` until the Customs Worker has obtained one (`manifests:read`)
  - `GET /manifests/{id}/elo/document` — The barcode PDF a driver presents at the French Smart Border, streamed through the authenticated API rather than as a blob URL (`manifests:read`)
  - `POST /manifests/{id}/document` — Request the document that travels with the vehicle. **Explicit, once the load is signed off** (`409` before approval): it used to be a side effect of approving (`manifests:write`)
  - `GET|PUT|DELETE /manifests/{id}/ens` and `GET /manifests/{id}/ens/filing-sheet` — **`410 Gone`**: the ENS moved onto the vehicle's declarations (below). Removed in [plan 15](docs/plans/15-manifest-signoff-lifecycle.md)
  - `POST /manifests/{id}/{transition}` — State transitions: `propose`, `approve`, `reject`, `prepare`, `ready`, `depart`, `deliver`, `lose`, `return`. **`approve` is Administrator-only and signs off the load: it confirms and freezes the manifest and files nothing** ([ADR 0004](docs/adr/0004-the-manifest-is-the-load-sign-off.md), [ADR 0006](docs/adr/0006-filing-is-manual-by-default.md)). It no longer needs an ENS
- `GET /convoys/{id}/vehicles/{vin}/declarations` — A vehicle's customs declarations ([ADR 0005](docs/adr/0005-declarations-are-per-vehicle-with-derived-staleness.md)): GMR, ENS, ELO and one Ukrainian goods list per receiver, withdrawn ones kept as history (`manifests:read`). Writes are `manifests:declare`, Administrator and Dispatcher
  - `POST /convoys/{id}/vehicles/{vin}/declarations/{gmr|elo|goods-list}/record` — **Manual filing**: record the reference obtained in the authority's portal, `{ "reference", "receiverRef"? }` (the receiver for a goods list only). **Write-once** — a second reference is `409`. An ELO is `409` until the vehicle has an accepted ENS. The ENS has its own route, below
  - `POST /convoys/{id}/vehicles/{vin}/declarations/{kind}/refused` — Record a refusal with a **bounded reason code only** (`data-error`, `goods-mismatch`, `missing-document`, `technical`, `other`); the authority's free text can quote the declaration, so it is never stored. A refused declaration can then be recorded afresh
  - `POST /convoys/{id}/vehicles/{vin}/declarations/{gmr|elo}/file` — **Automatic filing**, only where that authority's submission mode is `Automatic`; in manual mode (the default) it is `409` and says to record the reference instead. Needs the load signed off; the ELO needs an accepted ENS. Enqueues for the Customs Worker and marks the declaration `Filed` — the workers have no database, so *filed* means *enqueued*
  - `GET|PUT|DELETE /convoys/{id}/vehicles/{vin}/declarations/ens` — The ENS MRN this crossing was accepted under. **Recorded, not submitted** — Freedom does not talk to ICS2 — and **write-once**: `DELETE` withdraws it (keeping it as history) so a refiled declaration can be recorded
  - `GET /convoys/{id}/vehicles/{vin}/declarations/ens/filing-sheet` — Everything an **ICS2 Entry Summary Declaration** asks for that Freedom can know, plus a `missing` list of what it cannot. Deliberately carries **no delivery address**: the Ground Officer enters it in the portal (`consigneeAddressSource`)
  - `POST /convoys/{id}/vehicles/{vin}/declarations/{gmr|elo|goods-list|ens}/ready` — Mark a declaration **ready to file** (`manifests:declare`), which stores the load it was written from (the snapshot, [ADR 0005](docs/adr/0005-declarations-are-per-vehicle-with-derived-staleness.md)); a goods list names its receiver with `?receiverRef=`. `409` once it has been filed. A declaration recorded straight to filed is stamped with the load at that moment
  - `POST /convoys/{id}/vehicles/{vin}/declarations/{declarationId}/withdraw` — Withdraw a **stale** declaration (`manifests:declare`): it and its reference are kept as history and a new draft starts for the same scope. `409` while it still matches its load. A declaration reads `Stale` (in place of `Filed` or `Accepted`) whenever the vehicle's load differs from its snapshot; this is derived on read and never stored
- `GET /convoys/{id}/tasks` — The Dispatcher's **re-declare tasks** (`manifests:read`): one per stale declaration on the convoy, with the `resolution` that fits the instrument. Shown on screen, never emailed. "Declarations current" does not block departure yet (plan 13)
- `GET|POST /categories` — The categories donated items are sorted into, each with its hazard class, whether it is sensitive or not carried, how close to expiry an item counts as short-dated, and the customs code it maps to per authority (`ukCode`, `euCode`, `uaCode`). Reads are `categories:read` (every operational role); writes are **Administrator only** (`categories:write`)
  - `GET|PUT /categories/{id}` — Read a category, or change its names, flags and shelf-life rule. A built-in category stays built in
  - `PUT /categories/{id}/codes/{UK|EU|UA}` — Map a category to the code an authority wants; a `null` code clears it. Six to ten digits
- `GET|POST /donors` — Donors, who gave goods. A donor is a **split identity** like a volunteer: an anonymous key plus erasable details, entered by a Dispatcher or Loader because the donor has no login. Reads are `donations:read` (every operational role); writes are `donations:write` (Administrator, Dispatcher, Loader)
  - `GET|PUT|DELETE /donors/{id}` — Read or correct a donor. `DELETE` is an **erasure** (`donors:erase`, Administrator only): the details are deleted, the donations, their items and their value stay and read "Former donor". Never refused for being in use
  - `GET /donors/{id}/donations` — That donor's donations, newest first
  - `GET /donors/{id}/report` — The donor status report: what was given, its value by category and how far each item has got (`BeingPacked` or `PackedAndChecked`). **No receiver, region, route or address** — nothing about where it is going. Still readable after the donor is erased, under "Former donor", with the same totals
- `GET|POST /donations` — One donor, one drop-off, many items (`donations:read` / `donations:write`). `PUT /donations/{id}` changes the date and notes; `DELETE` is refused `409` while items still name it. A box item names its donation with an optional `donationId` on `POST /boxes/{id}/items` (`422 donation-not-found` for one that does not exist)
- `GET|POST /locations` — Distribution hubs (garages/warehouses); writes are **Administrator only**
  - `PUT|DELETE /locations/{id}` — Rename or remove a location
  - `GET|POST /locations/{id}/bays` — Bays within a location (code unique per location, not globally)
  - `PUT|DELETE /locations/{id}/bays/{bayId}` — Rename or remove a bay
- `PUT|GET|DELETE /boxes/{id}/bay` — Place, read or vacate a box's bay assignment (`PUT`/`DELETE` are **Loader only** — `boxes:allocate-bay`, narrower than `boxes:write`; `GET` is `boxes:read`)
  - `GET /boxes/{id}/bay/history` — Every bay the box has occupied, most recent first (`boxes:read`)

See `docs/local-authentication.md` for the full role/policy matrix.

The **operator UI (`web/`) covers every endpoint above** — all nine slices (donors, their donations and the printable donor report included, and the Administrator page for item categories and their customs codes included), every sub-resource
(convoy route/truck list/crew/insurance, box items/validate/bay, box QR label issue/print/revoke,
location bays, manifest crew/boxes/weight, the vehicle's declarations),
all nine manifest transitions, and the reason-gated receiver-detail flow — with nav and actions
gated by the same policy matrix (the API stays the enforcement point). The manifest's **Declarations**
tab shows the vehicle's GMR, ENS, ELO and goods lists, and records the reference a Dispatcher obtained
in each portal, marks a filed one refused with a bounded reason, files the GMR or ELO automatically
where that mode is on, and records and withdraws the ENS MRN (`manifests:declare`, Administrator and
Dispatcher). Approval no longer asks for any of it. The two ELO reads and the ENS filing sheet remain
without UI, read via `GET /manifests/{id}/elo`, `GET /manifests/{id}/elo/document` and
`GET /convoys/{id}/vehicles/{vin}/declarations/ens/filing-sheet` by hand.
The box detail page's **QR label** panel issues
a label, shows it inline and prints it (a print stylesheet reveals the label alone);
`/boxes/scan/{token}` is consumed by whatever scans the printed label, not the operator UI.

## Architecture

### Operator Web UI

- React + Vite SPA in `web/` (TypeScript strict), served **same-origin** by the API host
  under `/app` — built into `wwwroot/app` at image-build time, no separate deployable, no
  CORS. `Program.cs` serves it with `UseStaticFiles` + `MapFallbackToFile("/app/{*path}")`,
  scoped to `/app` so it never shadows an API route or health probe. Toggle with
  `Hosting__ServeStaticFrontend` (default on).
- Sign-in is **Authorization Code + PKCE** against the public Keycloak client `freedom-spa`
  (`iac/tofu/keycloak.tf`); the resulting JWT is sent as `Authorization: Bearer`. The API is
  unchanged — still a pure JWT resource server.
- Nav and actions are gated by the same 24-policy matrix the API enforces
  (`docs/local-authentication.md`); the API remains the enforcement point. Receiver street
  addresses are never rendered on any print/verification view.

### Authentication & Authorization

- JWT bearer tokens via OIDC (Keycloak locally, Microsoft Entra External ID in Azure)
- Role-based policies: `Administrator`, `Dispatcher`, `Loader`, `Purchaser`, `Mechanic`, `GroundOfficer`
- `Mechanic` is vehicles-only: it edits the fleet and records servicing inspections (`vehicles:service`, shared with Administrator), and nothing else
- **Critical**: `GroundOfficer` has segregated access to receiver delivery addresses only

### Security Boundaries

Three independent controls enforce receiver address segregation:
1. **Policy** — `receivers:detail` policy limits GroundOfficer access
2. **Identity** — `ISensitiveDbConnectionFactory` grants read access only to the ground officer role
3. **Database** — `DENY SELECT ON SCHEMA::sensitive TO freedom_app` — the app cannot read sensitive data

### Where the design is going

The sections below describe the system **as built**. Product discovery has since redesigned large parts of the
domain, recorded in [ADRs 0004–0017](docs/adr/README.md) and **not yet implemented**. In short:

| Area | As built | Target |
| --- | --- | --- |
| Manifest | Ten states; approval confirms and freezes it and files nothing (plan 08) | The Administrator's **load sign-off** only; a load change needs re-approval ([ADR 0004](docs/adr/0004-the-manifest-is-the-load-sign-off.md)) |
| Customs paperwork | One per-vehicle **Declaration** per instrument, **manual filing by default**; **staleness derived from a snapshot of the load** (plan 09) | One per-vehicle **Declaration** with derived staleness; **manual filing by default** ([ADRs 0005](docs/adr/0005-declarations-are-per-vehicle-with-derived-staleness.md), [0006](docs/adr/0006-filing-is-manual-by-default.md)) |
| Departure | Advisory readiness; only insurance is checked | Blocking requirements, **no override**, one convoy depart action ([ADR 0008](docs/adr/0008-readiness-is-computed-and-blocking-rules-are-not-overridden.md)) |
| Roles | Six global roles | Adds a **Convoy Leader** scoped to one convoy, with audited, time-limited address access, and Loaders scoped to their locations ([ADRs 0009](docs/adr/0009-convoy-leader-reads-destination-addresses.md), [0010](docs/adr/0010-resource-scoped-permissions.md)) |
| Boxes and items | Validation freezes a box; free-form item properties | Attested boxes are **replaced, never edited**, with a bilingual label; categories mapped to customs codes; GBP values; donors as an erasable split identity ([ADRs 0011](docs/adr/0011-attested-boxes-are-replaced-not-edited.md), [0013](docs/adr/0013-donors-are-a-split-identity.md), [0014](docs/adr/0014-items-are-classified-by-category-and-valued-in-gbp.md)) |
| Receivers | No status | Registration status gates destinations and departure ([ADR 0012](docs/adr/0012-receiver-registration-gates-convoys-and-boxes.md)) |

The business rules are in [`docs/domain/`](docs/domain/README.md) and every decision in
[`docs/domain/decisions.md`](docs/domain/decisions.md). The work is split into **19 plans** in
[`docs/plans/`](docs/plans/README.md), one branch and PR each, with an agent execution protocol and owner gates. The
target design is drawn as [sequence](docs/sequences/README.md), [process](docs/process/README.md),
[state](docs/states/README.md) and [use case](docs/use-cases/README.md) diagrams, a
[domain model](docs/model/domain-model.puml) and a [convoy timeline](docs/timeline/convoy-timeline.puml).

### Convoy and Manifest — what each one is for

The convoy is the unit that is **planned**; the manifest is the unit that is **executed per
vehicle**. The fact that ties them, "this vehicle is travelling with this convoy", is one row:
`dbo.ConvoyVehicle`, the truck list.

- **Convoy** — the journey: departure and expected arrival, the route, the truck list, the crew of
  each vehicle on it, their insurance, and arrival.
- **Truck list entry** (`dbo.ConvoyVehicle`) — one vehicle on one convoy. The crew, the insurance
  and the manifest all hang off it, so none of them can describe a truck that is not on the list.
  Withdrawal is a stamp, not a delete: a vehicle that breaks down leaves the convoy and may join a
  later one, but its manifest still describes a real load.
- **Manifest** — the document pack for that entry: cargo, border weight, GMR, ELO, ferry booking,
  delivery notes, and its own lifecycle. It carries **no crew**; `/manifests/{id}/crew` reads the
  convoy's.

### Manifest Lifecycle

As built, manifests follow a 10-state model (the as-built diagram is in git history at `f659eed`;
`docs/manifest-status.puml` now draws the target three-state sign-off, which
[plan 15](docs/plans/15-manifest-signoff-lifecycle.md) implements):
- Proposed → Confirmed (admin approval freezes it)
- Once confirmed, only progress states run: Preparing → Ready → InTransit → Delivered
- A confirmed manifest cannot be edited (backward transitions blocked)

### Border paperwork, and how it is obtained

Approving a manifest **only signs off the load** — it files, enqueues and hands off nothing
([ADR 0004](docs/adr/0004-the-manifest-is-the-load-sign-off.md)). The paperwork is a vehicle's
**declarations** ([ADR 0005](docs/adr/0005-declarations-are-per-vehicle-with-derived-staleness.md)), one
`dbo.Declaration` per vehicle per instrument on a single lifecycle (`Draft → ReadyToFile → Filed →
Accepted | Refused`, plus `Stale`, `Withdrawn` and `Closed`, which later plans set), and **filing is manual
by default** ([ADR 0006](docs/adr/0006-filing-is-manual-by-default.md)): a Dispatcher files in the
authority's portal and records the reference. Each authority that has a client has a **submission mode**,
`Customs__GmrSubmissionMode` and `Customs__EloSubmissionMode`, `Manual` unless set to `Automatic`; the ENS
and the Ukrainian goods list can never be automatic. In automatic mode, `POST .../declarations/{gmr|elo}/file`
puts the submission on a durable queue rather than calling out inside the HTTP request:

| What | Queue | Obtained by | Stored in |
| --- | --- | --- | --- |
| **GMR** — the UK Goods Movement Reference | `customs-work` | Customs Worker → HMRC GVMS | `gmr` container |
| **Manifest document** — travels with the vehicle | `manifest-documents` | Manifest Worker, on `POST /manifests/{id}/document` | `manifests` container |
| **ELO** — the French logistics envelope | `elo-envelopes` | Customs Worker → French customs | `elo` container |

The declaration is stamped `Filed` **after** the message is enqueued, and a failed enqueue leaves it
unfiled, so a retry is safe. Note what *filed* means here: the workers have no database, so in automatic
mode it means *enqueued*, not *accepted by the authority*. The local compose stack sets both modes to
`Automatic`, so the WireMock-backed environment and the BDD suite keep proving that path.

An **ELO** (*Enveloppe Logistique Obligatoire*) is required per transport unit at the French Smart
Border. It is not a goods declaration — it carries no cargo, weights, consignor, consignee or even a
registration. It is an index: crossing flags plus a list of declaration identifiers issued by other
customs systems. Freedom reads it back at `GET /manifests/{id}/elo`, and its barcode at
`/elo/document`.

The declaration identifier the envelope names is the **ENS MRN**, recorded against the manifest — which
is what closed the one gap the ELO integration shipped with. See
[`docs/schemas/edi/onboarding.md`](docs/schemas/edi/onboarding.md) for the DGDDI authorisation, user
agreement and certification run that no amount of code replaces.

### The ICS2 Entry Summary Declaration: recorded, not submitted

An **ENS** is the EU's pre-arrival safety-and-security declaration, lodged in ICS2, mandatory for road
carriers since September 2025. **The ELO cannot be created without its MRN**, so the order is
ENS → MRN → ELO → barcode → check-in, with no catching up at the border. For an accompanied movement
only one party may file, and it is the carrier — Ukrainian Action's own volunteers drive, so the charity
is the filer.

**Freedom records the MRN; it does not submit the declaration.** ICS2's Shared Trader Interface speaks
EU eDelivery **AS4** — SOAP over ebMS3, with an eIDAS sealing certificate registered in UUM&DS and a
mandatory conformance run — so the NSwag-generated-client pattern behind `EDI.ELO` and the two HMRC SDKs
does not transfer, and AS4 needs the permanently reachable *inbound* endpoint this design refuses
(`docs/recommendations.md` §4.1). There is therefore **no `src/ICS2.*` project and no queue**: a Ground
Officer files in the EU Customs Trader Portal and a Dispatcher records the MRN. `IEnsDeclarationStore`
is the seam an IT Service Provider's adapter drops into later, which is a procurement decision rather
than a coding one.

Three things fall out of that and are worth knowing before touching this slice:

- **`GET /convoys/{id}/vehicles/{vin}/declarations/ens/filing-sheet` is deliberately incomplete.** It composes the declarant and
  carrier EORI, consignor, office of first entry, mode of transport, active and passive means of
  transport, countries of routing, goods items with commodity codes, package counts and gross mass — and
  the consignee at **organisation and region only**. The delivery address lives in the `sensitive`
  schema, the sheet is composed on a connection that is `DENY SELECT`'d there, and a sheet listing
  Ukrainian addresses would be a targeting document. It sets `consigneeAddressWithheld` and says the Ground Officer enters the address
  in the portal, so a filer cannot conclude there is none. The sheet also
  reports its own gaps in `missing` — an unclassified item by description, a route stop with no ISO
  code, a ferry with no vessel IMO, boxes nobody has validated — because a gap found here costs a phone
  call and one found at the border costs a convoy.
- **An ELO needs an accepted ENS.** Approval is no longer gated on the ENS; the envelope is. Recording or
  filing an ELO is `409` until the vehicle has an `Accepted` ENS, and the envelope request names that MRN.
- **A recorded MRN is write-once, twice over.** The declaration row's reference is stamped by a
  conditional `UPDATE`, and the detail (who filed it, when ICS2 accepted it) is a blob at
  `declarations/{declarationId}.json` created with `IfNoneMatch = ETag.All`. A withdrawn declaration keeps
  its row and its blob, so the old MRN stays as history; its replacement is a new declaration with a new
  id. Several ENS fields are non-amendable, so invalidate-and-refile is the normal correction.

`dbo.Convoy` gained `CrossingMode` and `VesselImo` for this: mode of transport describes the **crossing**,
not the vehicle — a ferry sailing is maritime (1), a LeShuttle crossing is road (3), and rail is not
accepted at the Brexit Smart Border.

> **One prerequisite is not ours to solve.** An ENS declarant's EORI must be issued by an EU member
> state, and a GB EORI is not accepted. Ukrainian Action is UK-established, and the Commission's FAQ does
> not say what a non-EU carrier must do. Until that is settled with DGDDI or a customs agent, no
> declaration can be filed by anyone. See
> [`docs/schemas/ics2/onboarding.md`](docs/schemas/ics2/onboarding.md) and
> [`docs/adr/0003-ens-declaration-recorded-not-submitted.md`](docs/adr/0003-ens-declaration-recorded-not-submitted.md).

### Database

- **Declarative, not migrated.** `database/UA.Action.Freedom.Database` is an SDK-style SQL project
  (`Microsoft.Build.Sql`): one plain `CREATE` per table, the `sensitive` schema, the database roles
  and their `GRANT`/`DENY`s. It describes the end state only — no guards, no `ALTER`s, no data
  moves. `dotnet build` validates every reference and produces the dacpac; SqlPackage diffs it
  against the target and generates the change. No EF, no DbUp.
- **Deployed separately from code.** Locally the `db-deploy` compose service publishes it
  (`database/deploy.sh`); in CI it has its own workflow and versioned ghcr artifact.
- **Principals belong to the environment.** Logins, users and role membership are not in the
  dacpac: `iac/local/sql/principals.sql` (applied by `tofu apply`) creates `freedom_app` and
  `freedom_sensitive` locally; in Azure they are managed identities added by the deployment. The
  publish excludes users/logins/role membership so it never touches them.
- **Dev seed** — `database/seed/dev-seed.sql`, opt-in and fictional, with nothing in `sensitive.*`.

### Data Persistence

- **Dapper** for SQL mapping (typed constructor, rows map to primary constructor CLR types)
- One repository per slice with dedicated `I*Repository` port
- **CQRS read models** (flat shapes) separate from domain objects
- **Transactions** only where one fact spans several rows: route replacement; a convoy being
  cancelled (its truck list goes, and the crew and insurance cascade with it); a crew change (a
  removed driver leaves the insurance's covered drivers in the same transaction); convoy arrival (the stamp and the handover); volunteer add and
  erasure; receiver detail resolve + audit; QR-label re-issue and bay assignment. Taking a vehicle
  off an unpublished truck list needs none: the crew and the insurance cascade from the entry.
  See CLAUDE.md for the list.
- **VIN and manifest keys are `varchar(32)`** — pass them with `SqlKey.Of(...)`. Dapper's default
  `nvarchar` would make every key lookup a table scan under the SQL collation, and it caused
  deadlocks before it was fixed.

### Convoy crew, insurance, readiness and arrival

- **Crew** — drivers (registered to drive) and passengers (any volunteer), one seat per person per
  convoy, enforced by `UQ_ConvoyVehicleCrew_Convoy_Person`; there are no journey legs. This is the only crew record in the system: the
  manifest reads it rather than keeping its own.
- **Insurance** — per vehicle per convoy, naming the drivers it covers (`dbo.ConvoyVehicleInsuranceDriver`). Removing a driver
  keeps it in cover; a driver added afterwards is uncovered until it is recorded again.
  `TransitionManifestHandler` refuses `depart` without insurance in cover that names every driver.
- **Readiness** — `ConvoyReadiness.Assess`, one pure function, advisory only.
- **Withdrawal** — once the truck list is published a vehicle can still *leave*, it just cannot be
  erased: `DELETE /convoys/{id}/vehicles/{vin}?reason=` stamps `WithdrawnAt` and keeps everything.
  A withdrawn vehicle is skipped by readiness and by arrival, and is free to join a later convoy.
- **Arrival** — allowed once every vehicle still travelling has a finished manifest; stamps
  `Convoy.ArrivedAt` and hands Delivered/Lost vehicles over (`Vehicle.HandedOverAt`, never offered
  again), in one transaction. Nothing is *released*: a vehicle that was not handed over is free for
  the next convoy because this one has arrived, so the truck list survives as the record of who
  went. An arrived convoy's crew and insurance no longer change.

### Volunteer erasure (split identity)

`dbo.Person` holds only the anonymous key every foreign key points at; `dbo.PersonDetail` holds the
personal data. Erasure deletes the detail (UK data protection) and removes the key too unless past
records name it — they then show "Former volunteer". Refused while the volunteer is still crewing
a convoy that has not arrived, or a vehicle whose load is not yet delivered, lost or returned —
two questions of the one crew record, where they used to be two questions of two.

### Who last changed what

Every entity table carries `LastChangedBy` (a volunteer, or NULL for a change made by a login nobody had linked yet) and `LastChangedAt`, set by the same
statement as the change. The person comes from the login — `ChangeAttributionMiddleware` resolves it after authorization and the repositories stamp it —
never from a request body, and a login that is not linked to a volunteer is refused (`403 login-not-linked`) on every write except creating a volunteer and
linking a login. Reads show it as `lastChangedByName` / `lastChangedAt`, and the operator UI shows "Last changed by … on …" on the vehicle, volunteer, convoy,
box, receiver, location and manifest pages. A volunteer who has since been erased reads "Former volunteer" — decided once, in the `dbo.PersonDisplay` view.
It is *who changed it last*, not a history. A new table must carry both columns: `LastChangedGuardTests` fails the integration run otherwise, and lists the
tables that are exempt (links, and logs that already say who) with the reason.

### Vehicle servicing and the convoy gate

A vehicle's **inspection status** is the Mechanic's result, recorded through its own route
(`PUT /vehicles/{vin}/inspection`) and absent from the ordinary vehicle `PUT` and `INSERT`, so an
edit cannot set, clear or forge it. It gates convoy assignment: `ConvoyRepository.AssignVehicleAsync`
puts the rule in the `INSERT` itself (`WHERE InspectionStatus = Passed AND HandedOverAt IS NULL
AND NOT EXISTS (… an un-withdrawn entry on a convoy that has not arrived)`), so the database
settles a race with a Mechanic changing the result, and a
vehicle already on one convoy is never silently moved to another.

### Box QR labels

- `dbo.BoxQrCode` holds one row per label — an opaque, **non-enumerable** `Guid` token, the box
  it belongs to, and issue / revoke timestamps. The token is the only identifier printed on a
  physical label.
- A box can be re-labelled. Issuing a new code revokes any it already had (`IssueQrCodeAsync`,
  one transaction), so at most one row per box is active; revoked rows are kept as history. The
  "one active" rule lives in that method, not a filtered unique index (the old sqlcmd-applied
  schema script could not create one; the SqlPackage-published dacpac now could).
- `GET /boxes/scan/{token}` resolves an **active** token to its box — a revoked token reads as
  unknown. This is the link from the physical box to its digital record.
- The QR image and the printable label are rendered synchronously with **QRCoder** (managed
  `SvgQRCode` / `PngByteQRCode`, no `System.Drawing`, so nothing native in the Linux image) —
  `QrCodeRenderer` / `BoxLabelRenderer` in `src/UA.Action.Freedom.Api/Boxes/`. Both are pure and
  deterministic. The label renderer takes only a box id, a token and a date: it has no parameter
  through which a receiver, region or address could reach the label, so the redaction is
  structural (see `docs/domain/key-concepts.md` § Data Sensitivity).
- The QR encodes `{App:PublicBaseUrl}/boxes/scan/{token}`. `App:PublicBaseUrl` is
  environment-only config; when unset each request's own scheme + host are used (fine for
  `dotnet run`, wrong behind a proxy — the local simulation sets `App__PublicBaseUrl`
  explicitly).

### Bay allocation

- `Location` (a distribution hub) and `Bay` (a 1m by 1m storage area within one) are a new
  vertical slice, `dbo.Location` / `dbo.Bay`, that `dbo.Box.LocationId` now points at — replacing
  the loose `House`/`Street`/`City`/`Country`/`Postcode` columns a box used to carry directly. A
  bay's `Code` is unique per location, not globally (`UQ_Bay_Location_Code`).
- `dbo.BoxBayAssignment` records where a box currently sits (and has sat): mirrors
  `dbo.BoxQrCode`'s issue/revoke shape exactly. Assigning a box to a new bay vacates any bay it
  already occupied, as one transaction (`BoxRepository.AssignBayAsync`), so a box is never
  recorded as being in two bays at once; vacated rows are kept as history, not deleted.
- Placing or vacating a box's bay is **Loader only** (`boxes:allocate-bay`) — narrower even than
  `boxes:validate`, because this is the on-site, physical act of shelving a box, not a
  coordination task. `Location`/`Bay` CRUD (setting up a depot) is Administrator only
  (`locations:write`); reading either is open to every operational role.

## Development

### Test-Driven Development

This project follows strict TDD: every line of production code must respond to a failing test. See `CLAUDE.md` for detailed practices.

### Code Quality

- **TypeScript strict equivalents** via C# strict nullability
- **Zero warnings** — all build warnings are fixed, not suppressed
- **Functional style** — immutable data, pure functions, early returns
- **No comments** — code is self-documenting; comments added only for non-obvious WHYs

### Adding a New Slice

To add a new domain concept (e.g., a new `Donation` slice):

1. Define domain model in `src/UA.Action.Freedom.Domain/`
2. Create repository interface in `src/UA.Action.Freedom.Application/Donations/`
3. Write handlers (CQRS) in `src/UA.Action.Freedom.Application/Donations/`. Put the **success case
   first** in each outcome enum: command handlers are wrapped by `InstrumentedCommandHandler`, which
   reports member zero as `result="ok"` and any other member as `result="rejected"` on
   `freedom.handler.invocations` — no telemetry code is needed in the handler itself
4. Implement Dapper repository in `src/UA.Action.Freedom.Data/Donations/`. Take `IChangeAttribution` and set
   `LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()` in every `INSERT`/`UPDATE` (pass the parameters through
   `attribution.With(...)`), and read `LastChangedByName` through `ChangeStamp.ReadColumns/ReadJoin`
5. Create endpoints in `src/UA.Action.Freedom.Api/Donations/DonationEndpoints.cs`
6. Register in `Program.cs` via `AddFreedomApplication()` and `AddFreedomData()`
7. Add test suites: Unit, Component, Integration, and BDD feature files. Integration tests use
   the shared `SqlTestDatabase` helper (connects as `freedom_app`, skips when the database is
   down, fails instead when `FREEDOM_REQUIRE_INTEGRATION=true`). Component tests use
   `FreedomApi.With*`; the `InMemory*Repository` fake must enforce the same rules as the SQL —
   a fake kinder than the database lets a test pass that production fails.
8. Add the table as `database/UA.Action.Freedom.Database/dbo/Tables/Donation.sql` — a plain
   `CREATE TABLE` in its final shape, no guards, with `LastChangedBy` (FK to `dbo.Person`) and `LastChangedAt` (the schema guard test enforces it). `dbo` already carries the schema-level grants.
   Then `cd iac/local && docker compose build db-deploy && docker compose up -d --wait db-deploy`.
   Write CHECK constraints the way SQL Server stores them (see the gotchas doc), or every publish
   recreates them
9. Build the operator-UI slice — see the 8-step recipe in `web/README.md` (Zod schemas,
   `api/<slice>.ts` hooks, pages + routes, MSW handlers + factory, a Vitest Browser test per
   page, one `@smoke` Playwright spec). `src/pages/vehicles/` is the reference.

### Adding a durable hand-off to an external authority

Three exist — the GMR, the manifest document and the ELO envelope — and they are the same shape.
`src/UA.Action.Freedom.CustomsWorker/Elo/` is the most recent, and the one to copy:

1. A method on `IManifestWorkQueue` and a request record, in the Application. Keep the record
   narrow: a queue message is durable and widely readable, so anything it cannot carry is
   something that cannot leak.
2. A `HandOff(stage, …)` in the handler that causes it — today `FileDeclarationHandler`, behind the
   authority's submission mode. Stamp the declaration **after** the enqueue, never before.
3. A queue name in `QueueNames`, a storage queue and its poison queue in
   `iac/tofu/storage.tf` (append to `local.queues` — keep the single sequenced resource, because
   parallel `for_each` breaks Azurite), and the env vars on both containers in
   `docker-compose.yml`.
4. Worker side: a three-method port (`Receive` / `Complete` / `DeadLetter`), an Azure adapter that
   **copies to poison before deleting the original**, a wire record duplicated rather than shared,
   and a processor whose `ProcessNextAsync` returns `false` only on an empty queue.
5. Decide the three-way disposition deliberately and write the reasoning down: dead-letter what a
   retry cannot fix, complete what succeeded, and leave everything else for the visibility timeout.
   **If the call has a side effect at the far end, nothing after it may dead-letter** — see the ELO
   processor and `docs/adr/0002-elo-envelope-on-manifest-approval.md`.
6. Tests: a `*ProcessorTests` covering the six dispositions with the wire contract as a **JSON
   literal**, a `*TelemetryTests` with its own `Meter`, a producer-side wire-contract test in the
   Component project, and a health check so a missing queue fails readiness rather than silently
   swallowing work.

## Observability

All three services — `freedom-app`, `freedom-customs-worker`, `freedom-manifest-worker` — share one
wiring, `UA.Action.Freedom.Telemetry` (`AddFreedomTelemetry()`): traces, metrics and logs over OTLP
to whatever `OTEL_EXPORTER_OTLP_ENDPOINT` names. Locally that is the Grafana OTEL-LGTM container
(<http://localhost:3000>, no login); in Azure it is the collector or Application Insights ingest.
Unset, nothing is exported. Sampling is the SDK's own (`OTEL_TRACES_SAMPLER`, 100% locally).

- **Traces.** ASP.NET Core, HttpClient, SQL and the Azure Storage SDK, plus a span per command
  handler and per queue message. An approval's trace does not continue into the workers: the
  worker's span *links* to it (the producer writes `traceparent` into the queue message), because a
  retried message is processed minutes later. In Tempo, follow the link from the worker's
  `process customs-work` / `process manifest-documents` span.
- **Metrics.** The `freedom.*` business metrics — command outcomes, manifest transitions, queue
  depth/age/dispositions, GMR and ELO submission and dead-letter reasons, document rendering,
  worker-loop heartbeats — plus the ASP.NET Core, HttpClient and runtime built-ins. The full catalogue is in
  `iac/local/grafana/README.md`. Every tag is a bounded set; a person, receiver, plate, VIN or
  manifest reference is never a label.
- **Nothing sensitive in telemetry, by construction.** A span processor
  (`RedactingActivityProcessor`) records the *route* not the path, drops query strings, reduces
  client URLs to the peer, and blanks SQL statements on the `sensitive` schema. HMRC error bodies
  are never logged (status and type only), and nor are French customs' — only the bounded
  `FONC-ERR-00x` code, never the `libelleErreur` that quotes the declaration it objected to.
  `docs/gotchas-and-open-questions.md` § Observability.
- **Dashboards** (Grafana → folder *Freedom*, provisioned from `iac/local/grafana/dashboards/`):
  `.NET Runtime & HTTP`, `Freedom Application (API)`, `Customs Worker`, `Manifest Worker`, `Manifest Approval Pipeline`,
  `Convoy Operations`, `Access & Sensitive Data`.
- **Failures carry a `traceId`.** A 500 or 400 from the API returns the id of the trace that
  recorded it, so an operator can quote it back and find the request in Tempo.
- **Health checks** on `/health/live` and `/health/ready` (SQL, Blob, both work queues, the ICS2
  declaration store, OIDC). The declaration store has its own check because it is the one container
  the API *writes*: without it no ENS can be recorded, so no ELO can be filed.
  Probes are not traced or counted in the HTTP metrics.

## Known Issues & Gotchas

See `docs/gotchas-and-open-questions.md` for:
- MTP test runner CLI differences
- Integration test deadlock (assembly parallelization disabled)
- HMRC PPNS enum deserialization bug (codegen issue, affects real HMRC)
- The ELO integration (§5a): why UK → France is `IMPORT`, why TIR/ATA halves the paperwork, why
  nothing is dead-lettered once French customs has accepted an envelope, and why WireMock needs a
  restart after a mapping is edited
- The ELO declaration identifier is still a placeholder — the path works end to end against the
  local stub, but a real submission is refused until ICS2 supplies an ENS
- Database project rules: no migration code, principals excluded from publish, CHECK constraints
  in SQL Server's normalised form
- MSW mocks must mirror the API contract — a mock that accepts fields the API ignores hid the
  lost-inspection bug for a whole feature

## Contributing

1. Create a feature branch from `main`. If the work implements ADRs 0004–0017, take the matching plan in
   `docs/plans/` and follow its execution protocol: one plan per branch and PR, in dependency order.
2. Write failing tests first (TDD)
3. Implement the minimum to pass tests
4. Run all tests to ensure no regressions
5. Open a pull request — CI will build, test (Unit/Component), run the `web/` frontend job (typecheck/lint/format/test/build), and acceptance-test (Integration/BDD + Playwright smokes against the full stack), building the three service container images and running the suite against them. A change under `database/` also runs the `Database` workflow (dacpac build, fresh publish, no-op re-publish). Merging to `main` pushes those images (and the dacpac, from its own workflow) to `ghcr.io/thelastcolonial/*`, publishes the three SDK NuGet packages (`HMRC.GVMS`, `HMRC.PushPullNotifications`, `EDI.ELO`) to GitHub Packages, and cuts a GitHub Release annotated with the image digests.
6. Wait for approval and status checks to pass

## Resources

- **Domain concepts** — `docs/domain/key-concepts.md`, and the index of business rules in `docs/domain/README.md`
- **Decisions** — `docs/domain/decisions.md` (every decision, its rationale and the open questions)
- **Implementation plans** — `docs/plans/README.md` (index, dependency graph, gates and agent protocol)
- **Local authentication** — `docs/local-authentication.md`
- **Architecture & design** — `docs/recommendations.md`
- **Decision records** — `docs/adr/` (index in `docs/adr/README.md`; start with `0001-truck-list-as-a-table.md`)
- **Diagrams (target design)** — sequences `docs/sequences/`, processes `docs/process/` (start with
  `00-end-to-end-overview.puml`), states `docs/states/` and `docs/manifest-status.puml`, use cases `docs/use-cases/`,
  the domain model `docs/model/domain-model.puml`, and the convoy timeline `docs/timeline/convoy-timeline.puml`
- **System diagram** — `docs/c4/2-containers.puml`
- **HMRC API specs** — `docs/schemas/hmrc/`
- **French customs ELO (EDI) spec** — `docs/schemas/edi/`

## Support

For questions or issues, open a GitHub issue or contact the Ukrainian Action team.
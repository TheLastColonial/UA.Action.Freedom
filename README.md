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
├── UA.Action.Freedom.CustomsWorker/    # HMRC GMR submission & outcome collection
├── UA.Action.Freedom.ManifestWorker/   # Manifest document rendering
├── UA.Action.Freedom.Telemetry/         # Shared OpenTelemetry wiring, span redaction, queue & worker metrics
├── HMRC.GVMS/                          # HMRC Goods Vehicle Movements SDK
└── HMRC.PushPullNotifications/         # HMRC Push Pull Notifications SDK

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
└── HMRC.PushPullNotifications.Tests.Unit/

web/                                    # React + Vite operator UI (TypeScript, strict)
├── src/                                # App shell, api client, auth, pages per slice
├── e2e/                                # Playwright smokes against the running stack
└── ...                                 # built to web/dist, baked into the API image at /app

iac/
├── local/                              # Docker Compose substrate (all services)
└── tofu/                               # OpenTofu provisioning (resources)

docs/
├── domain/key-concepts.md              # Shared vocabulary & domain concepts
├── local-authentication.md             # Token & role setup guide
├── gotchas-and-open-questions.md       # Debugging & known traps
├── recommendations.md                  # Azure architecture & design decisions
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

Run the full local infrastructure (SQL, Blob/Queue Storage, Keycloak auth, HMRC mocks):

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

# (Optional) Load fictional seed data: depots, volunteers, a convoy, vehicles, boxes
cd ../local && docker compose up db-seed
```

The `db-seed` service is optional and re-runnable — it guards against seeding an already-populated
database. Integration/BDD suites create their own data and do not invoke it.

`tofu apply` also provisions the public PKCE Keycloak client (`freedom-spa`) the operator UI
signs in with. The UI is then at <http://localhost:8080/app/>; all three seed logins work
through the browser.

**Test logins** (all have password `password`):
- `admin` — Administrator role
- `operator` — Dispatcher, Loader, Mechanic, Purchaser roles
- `groundofficer` — GroundOfficer role (segregated access to delivery addresses)

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
- `GET|POST /vehicles` — Vehicle inventory (natural key: VIN), including optional cargo capacity (max weight, dimensions); writes are Administrator, Purchaser and Mechanic (`vehicles:write`)
  - `PUT /vehicles/{vin}/inspection` — Record the servicing inspection (`Pending`/`Inspecting`/`Passed`/`Failed` + notes) — **Administrator and Mechanic only** (`vehicles:service`). The ordinary `PUT /vehicles/{vin}` cannot change it — nor which convoy the vehicle is on, which only `/convoys/{id}/vehicles/{vin}` changes
- `GET|POST /people` — Volunteers & drivers; `DELETE /people/{id}` **erases** a volunteer (their personal data is deleted; past records show "Former volunteer"), refused with 409 while they are crewing a convoy that has not arrived or a vehicle whose load is not yet finished
- `GET|POST /convoys` — Convoy groups with routes
  - `PUT|GET /convoys/{id}/route` — Ordered stop list
  - `GET /convoys/{id}/vehicles` — The truck list, withdrawn vehicles included (each entry says which it is)
  - `PUT /convoys/{id}/vehicles/{vin}` — Put a vehicle on the truck list; only one that has **Passed** its inspection, has not been handed over, and is not travelling with another convoy may join (409 otherwise)
  - `DELETE /convoys/{id}/vehicles/{vin}` (`?reason=`) — Before publication this takes the vehicle off the list with its crew and insurance. **Afterwards it is a withdrawal**: the entry, its crew, its insurance and its manifest all stay, because a vehicle that breaks down still has paperwork describing a real load (`convoys:write`)
  - `GET|PUT|DELETE /convoys/{id}/vehicles/{vin}/crew/{personId}` — Vehicle crew, **per leg**: `{ "leg": "Uk" | "Border", "role": "Driver" | "Passenger" }`; a person takes one seat per leg, so a crew handover at the European border is recordable (**Dispatcher only** for `PUT`/`DELETE`; `GET`, which takes an optional `?leg=`, is in `convoys:read`)
  - `POST /convoys/{id}/vehicles/{vin}/manifest` — **Open the manifest for this vehicle on this convoy.** There is no `POST /manifests`: a manifest is the paperwork for a truck-list entry, and `(ConvoyId, Vin)` is a composite foreign key to it (`convoys:write`)
  - `GET|PUT|DELETE /convoys/{id}/vehicles/{vin}/insurance` — The vehicle's insurance for this convoy; any crew change voids it, and a manifest cannot depart without it (`convoys:write`)
  - `GET /convoys/{id}/readiness` — Advisory readiness: **two drivers on each leg** and insurance per vehicle, and a route. Withdrawn vehicles are skipped (`convoys:read`)
  - `POST /convoys/{id}/arrive` — Mark arrived once every vehicle still travelling has a finished manifest; Delivered/Lost vehicles are handed over for good (`convoys:write`)
  - `POST /convoys/{id}/publish-truck-list` — Close the truck list to additions
- `GET|POST /receivers` — Delivery contacts (reference/org/region)
  - `GET|PUT /receivers/{ref}/detail` — **GroundOfficer only**: delivery address + contact
- `GET|POST /boxes` — Packing containers
  - `GET|POST|DELETE /boxes/{id}/items` — Item inventory
  - `POST /boxes/{id}/validate` — Lock box weight and optional dimensions
  - `POST|GET|DELETE /boxes/{id}/qr-code` — Issue / read / revoke the box's QR label (`boxes:write` to issue and revoke, `boxes:read` to read)
  - `GET /boxes/{id}/qr-code/image` (`?format=svg\|png`) — The QR image alone (`boxes:read`)
  - `GET /boxes/{id}/label` — Printable SVG label: QR + box number, no receiver detail (`boxes:read`)
  - `GET /boxes/scan/{token}` — Resolve a scanned token to its box (`boxes:read`)
- `GET /manifests` — The document pack for one vehicle on one convoy: cargo, border weight, GMR, ferry booking. **Created on its convoy** (above), not here
  - `PUT /manifests/{id}` — Notes and ferry booking only. The convoy and the vehicle are the manifest's identity, so there is no field for either
  - `GET /manifests/{id}/crew` — Who is travelling with its vehicle, per leg. A **read**: crewing happens once, on the truck-list entry
  - `GET|PUT|DELETE /manifests/{id}/boxes/{boxId}` — Cargo assignment
  - `POST /manifests/{id}/{transition}` — State transitions: `propose`, `approve`, `reject`, `prepare`, `ready`, `depart`, `deliver`, `lose`, `return`
- `GET|POST /locations` — Distribution hubs (garages/warehouses); writes are **Administrator only**
  - `PUT|DELETE /locations/{id}` — Rename or remove a location
  - `GET|POST /locations/{id}/bays` — Bays within a location (code unique per location, not globally)
  - `PUT|DELETE /locations/{id}/bays/{bayId}` — Rename or remove a bay
- `PUT|GET|DELETE /boxes/{id}/bay` — Place, read or vacate a box's bay assignment (`PUT`/`DELETE` are **Loader only** — `boxes:allocate-bay`, narrower than `boxes:write`; `GET` is `boxes:read`)
  - `GET /boxes/{id}/bay/history` — Every bay the box has occupied, most recent first (`boxes:read`)

See `docs/local-authentication.md` for the full role/policy matrix.

The **operator UI (`web/`) covers every endpoint above** — all seven slices, every sub-resource
(convoy route/truck list/crew/insurance, box items/validate/bay, box QR label issue/print/revoke,
location bays, manifest crew/boxes/weight), all nine manifest transitions, and the reason-gated
receiver-detail flow —
with nav and actions gated by the same policy matrix (the API stays the enforcement point). The
box detail page's **QR label** panel issues a label, shows it inline and prints it (a print
stylesheet reveals the label alone); `/boxes/scan/{token}` is consumed by whatever scans the
printed label, not the operator UI.

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
- Nav and actions are gated by the same 20-policy matrix the API enforces
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

Manifests follow a 10-state model (see `docs/manifest-status.puml`):
- Proposed → Confirmed (admin approval freezes it)
- Once confirmed, only progress states run: Preparing → Ready → InTransit → Delivered
- A confirmed manifest cannot be edited (backward transitions blocked)

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
  cancelled (its truck list goes, and the crew and insurance cascade with it); a crew change (it
  voids the vehicle's insurance); convoy arrival (the stamp and the handover); volunteer add and
  erasure; receiver detail resolve + audit; QR-label re-issue and bay assignment. Taking a vehicle
  off an unpublished truck list needs none: the crew and the insurance cascade from the entry.
  See CLAUDE.md for the list.
- **VIN and manifest keys are `varchar(32)`** — pass them with `SqlKey.Of(...)`. Dapper's default
  `nvarchar` would make every key lookup a table scan under the SQL collation, and it caused
  deadlocks before it was fixed.

### Convoy crew, insurance, readiness and arrival

- **Crew** — drivers (registered to drive) and passengers (any volunteer), assigned **per leg** of
  the journey; one seat per person per leg, enforced by
  `UQ_ConvoyVehicleCrew_Convoy_Person_Leg`. This is the only crew record in the system: the
  manifest reads it rather than keeping its own.
- **Insurance** — per vehicle per convoy, naming the crew. A crew change voids it in the same
  transaction; `TransitionManifestHandler` refuses `depart` without insurance in cover.
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
4. Implement Dapper repository in `src/UA.Action.Freedom.Data/Donations/`
5. Create endpoints in `src/UA.Action.Freedom.Api/Donations/DonationEndpoints.cs`
6. Register in `Program.cs` via `AddFreedomApplication()` and `AddFreedomData()`
7. Add test suites: Unit, Component, Integration, and BDD feature files. Integration tests use
   the shared `SqlTestDatabase` helper (connects as `freedom_app`, skips when the database is
   down, fails instead when `FREEDOM_REQUIRE_INTEGRATION=true`). Component tests use
   `FreedomApi.With*`; the `InMemory*Repository` fake must enforce the same rules as the SQL —
   a fake kinder than the database lets a test pass that production fails.
8. Add the table as `database/UA.Action.Freedom.Database/dbo/Tables/Donation.sql` — a plain
   `CREATE TABLE` in its final shape, no guards. `dbo` already carries the schema-level grants.
   Then `cd iac/local && docker compose build db-deploy && docker compose up -d --wait db-deploy`.
   Write CHECK constraints the way SQL Server stores them (see the gotchas doc), or every publish
   recreates them
9. Build the operator-UI slice — see the 8-step recipe in `web/README.md` (Zod schemas,
   `api/<slice>.ts` hooks, pages + routes, MSW handlers + factory, a Vitest Browser test per
   page, one `@smoke` Playwright spec). `src/pages/vehicles/` is the reference.

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
  depth/age/dispositions, GMR submission and dead-letter reasons, document rendering, worker-loop
  heartbeats — plus the ASP.NET Core, HttpClient and runtime built-ins. The full catalogue is in
  `iac/local/grafana/README.md`. Every tag is a bounded set; a person, receiver, plate, VIN or
  manifest reference is never a label.
- **Nothing sensitive in telemetry, by construction.** A span processor
  (`RedactingActivityProcessor`) records the *route* not the path, drops query strings, reduces
  client URLs to the peer, and blanks SQL statements on the `sensitive` schema. HMRC error bodies
  are never logged (status and type only). `docs/gotchas-and-open-questions.md` § Observability.
- **Dashboards** (Grafana → folder *Freedom*, provisioned from `iac/local/grafana/dashboards/`):
  `.NET Runtime & HTTP`, `Freedom Application (API)`, `Customs Worker`, `Manifest Worker`, `Manifest Approval Pipeline`,
  `Convoy Operations`, `Access & Sensitive Data`.
- **Failures carry a `traceId`.** A 500 or 400 from the API returns the id of the trace that
  recorded it, so an operator can quote it back and find the request in Tempo.
- **Health checks** on `/health/live` and `/health/ready` (SQL, Blob, Queue, OIDC). Probes are not
  traced or counted in the HTTP metrics.

## Known Issues & Gotchas

See `docs/gotchas-and-open-questions.md` for:
- MTP test runner CLI differences
- Integration test deadlock (assembly parallelization disabled)
- HMRC PPNS enum deserialization bug (codegen issue, affects real HMRC)
- Database project rules: no migration code, principals excluded from publish, CHECK constraints
  in SQL Server's normalised form
- MSW mocks must mirror the API contract — a mock that accepts fields the API ignores hid the
  lost-inspection bug for a whole feature

## Contributing

1. Create a feature branch from `main`
2. Write failing tests first (TDD)
3. Implement the minimum to pass tests
4. Run all tests to ensure no regressions
5. Open a pull request — CI will build, test (Unit/Component), run the `web/` frontend job (typecheck/lint/format/test/build), and acceptance-test (Integration/BDD + Playwright smokes against the full stack), building the three service container images and running the suite against them. A change under `database/` also runs the `Database` workflow (dacpac build, fresh publish, no-op re-publish). Merging to `main` pushes those images (and the dacpac, from its own workflow) to `ghcr.io/thelastcolonial/*`, publishes the two HMRC SDK NuGet packages to GitHub Packages, and cuts a GitHub Release annotated with the image digests.
6. Wait for approval and status checks to pass

## Resources

- **Domain concepts** — `docs/domain/key-concepts.md`
- **Local authentication** — `docs/local-authentication.md`
- **Architecture & design** — `docs/recommendations.md`
- **Decision records** — `docs/adr/` (start with `0001-truck-list-as-a-table.md`)
- **State diagram** — `docs/manifest-status.puml`
- **System diagram** — `docs/c4/2-containers.puml`
- **HMRC API specs** — `docs/schemas/hmrc/`

## Support

For questions or issues, open a GitHub issue or contact the Ukrainian Action team.
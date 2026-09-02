# UA.Action.Freedom

Automation to support Ukrainian Action, a charity that runs supply convoys (donated vehicles + cargo) from the UK to Ukraine. This system models the complete lifecycle of preparing a convoy: sourcing vehicles, packing boxes of items, assigning driver teams, building manifests per vehicle, and tracking convoy routes and status through to delivery.

Built on .NET 10 with ASP.NET Core minimal APIs, Dapper for data access, and OpenTelemetry for observability.

## Getting Started

### Prerequisites

- **.NET 10 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet)
- **Node 22 LTS** — for the operator web UI (`web/`); `nvm use` picks it up from `web/.nvmrc`
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
├── HMRC.GVMS/                          # HMRC Goods Vehicle Movements SDK
└── HMRC.PushPullNotifications/         # HMRC Push Pull Notifications SDK

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
# Start Docker containers
cd iac/local
cp .env.example .env
docker compose up -d --wait

# Provision resources via OpenTofu
cd ../tofu
tofu init
tofu apply

# Verify all services are healthy
curl http://localhost:8080/health/ready
```

`tofu apply` also provisions the public PKCE Keycloak client (`freedom-spa`) the operator UI
signs in with. The UI is then at <http://localhost:8080/app/>; all three seed logins work
through the browser.

**Test logins** (all have password `password`):
- `admin` — Administrator role
- `operator` — Dispatcher, Loader, Purchaser roles
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

### API Endpoints

Core resource endpoints:
- `GET|POST /vehicles` — Vehicle inventory (natural key: VIN)
- `GET|POST /people` — Volunteers & drivers
- `GET|POST /convoys` — Convoy groups with routes
  - `PUT|GET /convoys/{id}/route` — Ordered stop list
  - `PUT|DELETE /convoys/{id}/vehicles/{vin}` — Vehicle assignment
  - `POST /convoys/{id}/publish-truck-list` — Lock vehicle manifest
- `GET|POST /receivers` — Delivery contacts (reference/org/region)
  - `GET|PUT /receivers/{ref}/detail` — **GroundOfficer only**: delivery address + contact
- `GET|POST /boxes` — Packing containers
  - `GET|POST|DELETE /boxes/{id}/items` — Item inventory
  - `POST /boxes/{id}/validate` — Lock box weight
  - `POST|GET|DELETE /boxes/{id}/qr-code` — Issue / read / revoke the box's QR label (`boxes:write` to issue and revoke, `boxes:read` to read)
  - `GET /boxes/{id}/qr-code/image` (`?format=svg\|png`) — The QR image alone (`boxes:read`)
  - `GET /boxes/{id}/label` — Printable SVG label: QR + box number, no receiver detail (`boxes:read`)
  - `GET /boxes/scan/{token}` — Resolve a scanned token to its box (`boxes:read`)
- `GET|POST /manifests` — Vehicle + drivers + cargo units
  - `GET|PUT /manifests/{id}/teams/{leg}` — Driver team assignment
  - `GET|PUT|DELETE /manifests/{id}/boxes/{boxId}` — Cargo assignment
  - `POST /manifests/{id}/{transition}` — State transitions: `propose`, `approve`, `reject`, `prepare`, `ready`, `depart`, `deliver`, `lose`, `return`

See `docs/local-authentication.md` for the full role/policy matrix.

The **operator UI (`web/`) covers every endpoint above** — all six slices, every sub-resource
(convoy route/vehicles, box items/validate, box QR label issue/print/revoke, manifest
teams/boxes/weight), all nine manifest transitions, and the reason-gated receiver-detail flow —
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
- Nav and actions are gated by the same 15-policy matrix the API enforces
  (`docs/local-authentication.md`); the API remains the enforcement point. Receiver street
  addresses are never rendered on any print/verification view.

### Authentication & Authorization

- JWT bearer tokens via OIDC (Keycloak locally, Microsoft Entra External ID in Azure)
- Role-based policies: `Administrator`, `Dispatcher`, `Loader`, `Purchaser`, `GroundOfficer`
- **Critical**: `GroundOfficer` has segregated access to receiver delivery addresses only

### Security Boundaries

Three independent controls enforce receiver address segregation:
1. **Policy** — `receivers:detail` policy limits GroundOfficer access
2. **Identity** — `ISensitiveDbConnectionFactory` grants read access only to the ground officer role
3. **Database** — `DENY SELECT ON SCHEMA::sensitive TO freedom_app` — the app cannot read sensitive data

### Manifest Lifecycle

Manifests follow a 10-state model (see `docs/manifest-status.puml`):
- Proposed → Confirmed (admin approval freezes it)
- Once confirmed, only progress states run: Preparing → Ready → InTransit → Delivered
- A confirmed manifest cannot be edited (backward transitions blocked)

### Data Persistence

- **Dapper** for SQL mapping (typed constructor, rows map to primary constructor CLR types)
- One repository per slice with dedicated `I*Repository` port
- **CQRS read models** (flat shapes) separate from domain objects
- **Transactions** only where required: route replacement, and QR-label re-issue
  (`BoxRepository.IssueQrCodeAsync` revokes the old row and inserts the new one atomically —
  see below)

### Box QR labels

- `dbo.BoxQrCode` holds one row per label — an opaque, **non-enumerable** `Guid` token, the box
  it belongs to, and issue / revoke timestamps. The token is the only identifier printed on a
  physical label.
- A box can be re-labelled. Issuing a new code revokes any it already had (`IssueQrCodeAsync`,
  one transaction), so at most one row per box is active; revoked rows are kept as history. The
  "one active" rule lives in that method, not a filtered unique index — `001-schemas.sql` runs
  under sqlcmd with `QUOTED_IDENTIFIER` OFF and avoids filtered indexes throughout.
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
3. Write handlers (CQRS) in `src/UA.Action.Freedom.Application/Donations/`
4. Implement Dapper repository in `src/UA.Action.Freedom.Data/Donations/`
5. Create endpoints in `src/UA.Action.Freedom.Api/Donations/DonationEndpoints.cs`
6. Register in `Program.cs` via `AddFreedomApplication()` and `AddFreedomData()`
7. Add test suites: Unit, Component, Integration, and BDD feature files
8. Update schema in `iac/local/sql/001-schemas.sql` and re-run `tofu apply`
9. Build the operator-UI slice — see the 8-step recipe in `web/README.md` (Zod schemas,
   `api/<slice>.ts` hooks, pages + routes, MSW handlers + factory, a Vitest Browser test per
   page, one `@smoke` Playwright spec). `src/pages/vehicles/` is the reference.

## Observability

- **OpenTelemetry** instrumentation (traces, metrics, logs)
- **Local exporter** to Grafana OTEL-LGTM container
- **Azure exporter** to Application Insights (via `OTEL_EXPORTER_OTLP_ENDPOINT`)
- **Health checks** on `/health/live` and `/health/ready` (SQL, Blob, Queue, OIDC)

## Known Issues & Gotchas

See `docs/gotchas-and-open-questions.md` for:
- MTP test runner CLI differences
- Integration test deadlock (assembly parallelization disabled)
- HMRC PPNS enum deserialization bug (codegen issue, affects real HMRC)
- SQL `QUOTED_IDENTIFIER` quirks with sqlcmd

## Contributing

1. Create a feature branch from `main`
2. Write failing tests first (TDD)
3. Implement the minimum to pass tests
4. Run all tests to ensure no regressions
5. Open a pull request — CI will build, test (Unit/Component), run the `web/` frontend job (typecheck/lint/format/test/build), and acceptance-test (Integration/BDD + Playwright smokes against the full stack), building the three service container images and running the suite against them. Merging to `main` pushes those images to `ghcr.io/thelastcolonial/*`, publishes the two HMRC SDK NuGet packages to GitHub Packages, and cuts a GitHub Release annotated with the image digests.
6. Wait for approval and status checks to pass

## Resources

- **Domain concepts** — `docs/domain/key-concepts.md`
- **Local authentication** — `docs/local-authentication.md`
- **Architecture & design** — `docs/recommendations.md`
- **State diagram** — `docs/manifest-status.puml`
- **System diagram** — `docs/c4/2-containers.puml`
- **HMRC API specs** — `docs/schemas/hmrc/`

## Support

For questions or issues, open a GitHub issue or contact the Ukrainian Action team.
# UA.Action.Freedom

Automation to support Ukrainian Action, a charity that runs supply convoys (donated vehicles + cargo) from the UK to Ukraine. This system models the complete lifecycle of preparing a convoy: sourcing vehicles, packing boxes of items, assigning driver teams, building manifests per vehicle, and tracking convoy routes and status through to delivery.

Built on .NET 10 with ASP.NET Core minimal APIs, Dapper for data access, and OpenTelemetry for observability.

## Getting Started

### Prerequisites

- **.NET 10 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet)
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

**Test logins** (all have password `password`):
- `admin` — Administrator role
- `operator` — Dispatcher, Loader, Purchaser roles
- `groundofficer` — GroundOfficer role (segregated access to delivery addresses)

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
- `GET|POST /manifests` — Vehicle + drivers + cargo units
  - `GET|PUT /manifests/{id}/teams/{leg}` — Driver team assignment
  - `GET|PUT|DELETE /manifests/{id}/boxes/{boxId}` — Cargo assignment
  - `POST /manifests/{id}/{transition}` — State transitions: `propose`, `approve`, `reject`, `prepare`, `ready`, `depart`, `deliver`, `lose`, `return`

See `docs/local-authentication.md` for the full role/policy matrix.

## Architecture

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
- **Transactions** only where required (route replacement as atomic unit)

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
5. Open a pull request — CI will build, test (Unit/Component), and acceptance-test (Integration/BDD + full stack)
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
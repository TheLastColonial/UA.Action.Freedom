# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

UA.Action.Freedom — automation to support Ukrainian Action, a charity that runs supply convoys (donated vehicles + cargo) from the UK to Ukraine. The domain models the lifecycle of preparing a convoy: sourcing vehicles, packing boxes of items, assigning driver teams, building a manifest per vehicle, and tracking the convoy's route and status through to delivery.

## Commands

Built on .NET 10 (`net10.0`), solution file is `UA.Action.Freedom.slnx`.

```
dotnet build UA.Action.Freedom.slnx                                  # build everything
dotnet test --solution UA.Action.Freedom.slnx                        # run all test projects
dotnet test --project tests/UA.Action.Freedom.Tests.Unit/UA.Action.Freedom.Tests.Unit.csproj   # run one test project
dotnet test --project <proj> --filter-query "/*/*/ClassName/MethodName"                         # run a single test
dotnet run --project src/UA.Action.Freedom.Api/UA.Action.Freedom.Api.csproj          # run the API alone (http://localhost:5100)

cd iac/local && cp .env.example .env && docker compose up -d --wait   # local Azure simulation: substrate
cd iac/tofu  && tofu init && tofu apply                              # ...then its resources
curl http://localhost:8080/health/ready                              # all Healthy == wired up correctly
```

`dotnet test` runs in **Microsoft.Testing.Platform (MTP) mode**, opted in via `global.json`
(`{ "test": { "runner": "Microsoft.Testing.Platform" } }`) because the .NET 10 SDK dropped the
legacy VSTest bridge that `xunit.v3` relied on. MTP mode changes the CLI: pass the solution as
`--solution` and a project as `--project` (bare paths are no longer positional). Filter with
xUnit v3's MTP options — `--filter-class "*Name"`, `--filter-method "*Name"`,
`--filter-namespace`, `--filter-trait "k=v"`, or `--filter-query "/asm/ns/class/method"`
(the [query filter language](https://xunit.net/docs/query-filter-language)) — **not** the plain
`--filter` / `FullyQualifiedName~...` VSTest syntax, which silently matches nothing here.
`--configuration`, `--no-build`, `--no-restore`, `--verbosity` still work as before.

Test framework is xUnit v3 with `AwesomeAssertions` (fluent assertions), `NSubstitute` (mocking), and `MELT` (testable `ILogger`). `Xunit` is globally usable via implicit `<Using>` in each test csproj — no `using Xunit;` needed.

The solution builds clean. It emits ~27 `CS8618` nullability warnings from the Domain entities (non-nullable properties without `required`); those are pre-existing and not treated as errors.

## Architecture

Solution follows a layered structure under `src/`:

- **`UA.Action.Freedom.Domain`** — plain C# domain model, no framework dependencies. This is where the business concepts live (see below).
- **`UA.Action.Freedom.Application`** — use cases / orchestration. Hand-rolled CQRS: `Abstractions/ICommandHandler<,>` + `IQueryHandler<,>`, one handler per use case, all registered explicitly in `AddFreedomApplication()` (no MediatR, no assembly scanning). First slice is `Vehicles/` (create/update/delete commands, get/list queries) behind the `IVehicleRepository` port. Handlers return small outcome enums (`Created`/`Conflict`, `Updated`/`NotFound`) so the API maps status codes without exceptions. `VehicleReadModel` is the read + write shape — the anemic `Domain.Vehicle` (non-nullable `Convoy`/`Purchaser` nav, no `Id`) can't be hydrated from a row yet.
- **`UA.Action.Freedom.Data`** — persistence. `SqlConnectionFactory` (`IDbConnectionFactory`) builds `SqlConnection`s from `ConnectionStrings:Freedom`, same key as the health check. `Vehicles/VehicleRepository` is Dapper over `dbo.Vehicle`, registered by `AddFreedomData()`. **Dapper's constructor-mapping is strict**: `dbo.Vehicle` uses `int` (not `tinyint`/`smallint`) for the enum and year columns so they line up with the CLR types of `VehicleReadModel`'s primary constructor.
- **`UA.Action.Freedom.Api`** — ASP.NET Core minimal API host (`Program.cs`). `/vehicles` CRUD (`Vehicles/VehicleEndpoints.cs`, `MapFreedomVehicles()`) — natural key on VIN, so the route is `/vehicles/{vin}`. JWT bearer auth (`Configuration/AuthenticationExtensions.cs`, `AddFreedomAuthentication`/`AddFreedomAuthorization`) driven by `OidcOptions`: `RoleClaimType = "roles"`, `MapInboundClaims = false` (Keycloak/Entra send a flat `roles` claim — without this it gets rewritten to the WS-Fed role URI and every policy fails), `RequireHttpsMetadata` off in Development, audience checked only when `Oidc:Audience` is set (Keycloak issues `aud: account`). Policies: `vehicles:read` (Administrator/Purchaser/Dispatcher/Loader), `vehicles:write` (Administrator/Purchaser); GroundOfficer excluded. Request bodies validated by FluentValidation via `Configuration/ValidationFilter<T>`; errors are `ProblemDetails` (`AddProblemDetails()` + `UseExceptionHandler()`). Also real configuration (`Configuration/`, environment-variable only), `/health/live` + `/health/ready` probes that genuinely check SQL, blob, queue and OIDC discovery (`Health/`), and data-protection keys persisted to blob storage. OpenTelemetry traces, metrics and logs are wired in `Installer/TelemetryInstaller.cs` (`builder.AddFreedomTelemetry()`): ASP.NET Core + HttpClient + runtime instrumentation, exported over OTLP to whatever `OTEL_EXPORTER_OTLP_ENDPOINT` names (the Grafana OTEL-LGTM container locally, App Insights in Azure). The exporter is skipped when that variable is unset, so `dotnet run` and the in-memory component tests collect but do not ship. References `FluentValidation` for future use.
- **`UA.Action.Freedom.CustomsWorker`** — the Azure Function of the C4 diagram, as a plain `BackgroundService` so it runs anywhere. `GmrSubmissionProcessor` drains the customs work queue and submits to HMRC; `GmrOutcomeCollector` polls the Push Pull Notifications box for outcomes (pull only — no inbound endpoint, per `recommendations.md` §4.1) and writes GMR documents to blob storage. Storage and queue access sit behind ports (`ICustomsWorkQueue`, `IGmrDocumentStore`) so the rules about *when a queue message is deleted* — the part that loses a convoy's GMR if wrong — are unit-testable without a storage account.
- **`HMRC.GVMS`** (`src/HMRC.GVMS`) / **`HMRC.PushPullNotifications`** (`src/HMRC.PushPullNotifications`) — NSwag-generated typed HTTP clients for the HMRC Goods Vehicle Movements and Push Pull Notifications APIs. These are standalone HMRC SDKs and deliberately carry no `UA.Action.Freedom` prefix — namespace and assembly name match the project name. The `Generated/` folder is committed codegen output; regenerate with `build/nswag/regenerate.ps1 -Api <goods-vehicle-movements|push-pull-notifications>` and see `build/nswag/README.md`. Each project adds a hand-written `*ClientOptions` + `Add*Client(this IServiceCollection)` DI extension (typed `HttpClient`, HMRC versioned `Accept` header, caller supplies the OAuth handler). Do not hand-edit `Generated/`. Their unit tests live in `tests/HMRC.GVMS.Tests.Unit` and `tests/HMRC.PushPullNotifications.Tests.Unit` — one dedicated test project per SDK, not folded into `UA.Action.Freedom.Tests.Unit`.

  > **Known bug: `HMRC.PushPullNotifications` cannot deserialise a notification.** HMRC sends `"messageContentType": "application/json"`; NSwag generated the enum with `[EnumMember(Value = "application/json")]` but decorated the property with `JsonStringEnumConverter<MessageContentType>`, which matches C# member names (`Application_json`) and ignores `[EnumMember]`. Every response throws `JsonException`. This affects real HMRC, not just the local stub, so do not "fix" it by changing the stub. The fix belongs in `build/nswag/` plus a regeneration. Reproduce with the local environment — `iac/README.md` § Known issues.

Six test projects under `tests/`: `HMRC.GVMS.Tests.Unit` and `HMRC.PushPullNotifications.Tests.Unit` hold the SDK client tests. Of the four `UA.Action.Freedom.Tests.*` projects: `Unit` covers the CustomsWorker and the Application vehicle handlers (`Vehicles/`, NSubstitute `IVehicleRepository`); `Component` hosts the API with `WebApplicationFactory<Program>` and, for `/vehicles`, swaps in `InMemoryVehicleRepository` + a `TestAuthHandler` scheme (`FreedomApi.WithVehicles(...)`) to exercise the authz split and validation without Keycloak or SQL; `Integration` (`Vehicles/VehicleRepositoryTests`) runs the real Dapper repository against `dbo.Vehicle`, self-skipping via `Assert.Skip` when the DB is unreachable (so it is CI-safe until a SQL service container is added); `BDD` is **Reqnroll** (`Reqnroll.xunit.v3`) — `Features/Vehicles.feature` drives HTTP against the **deployed containers** (edge on `http://localhost:8080`, Keycloak on `http://localhost:8081`, all overridable by `FREEDOM_*` env vars), acquiring real tokens by password grant, and `Assert.SkipUnless` skips the whole feature when that stack is not up or its image predates `/vehicles`. After a code change, rebuild the image (`cd iac/local && docker compose build app && docker compose up -d --wait app edge`) before running the BDD suite against it.

### Domain model (`src/UA.Action.Freedom.Domain`)

Core entities and how they relate:

- **`Person`** — base type for any individual (name, DOB, join date, phone). `Driver : Person` adds `Convoys` and `Committed`. `Reciever` (the delivery contact) wraps a `Person` with an `Address` and `Organisation`.
- **`Vehicle`** — a donated vehicle (VIN, plate, weight, fuel/transmission type, purchaser, drivers, and its `Convoy`). Vehicles are themselves part of the aid — they are handed over in Ukraine, not driven back.
- **`Convoy`** — a group of `Vehicle`s travelling together, with a `Route` (ordered list of `Address` stops) and start/expected-end timestamps.
- **`DriverTeam`** — a `PrimaryDriver` + `SecondaryDriver` pair. A `Manifest` has two teams: `DriverUK` (UK→Europe leg) and `DriverBorder` (Europe→Ukraine leg).
- **`Box`** — a packed container of `Item`s with a confirmed weight, validation state (`ValidatedBy`/`ValidatedAt`), current `Location`, and target `Reciever`.
- **`Manifest`** — the unit that ties a `Vehicle`, both `DriverTeam`s, and the `Box[]` cargo together for one convoy leg. `TotalWeightKg()` sums vehicle + cargo + a fixed 200kg (2 drivers + bags) + 45kg fuel allowance — this constant-padding convention is intentional (border-check estimate), not a bug.

**Spelling conventions:** the codebase is mid-way through correcting misspelled type names. `Veichle` → `Vehicle` is done apart from the two stragglers breaking the build (see Commands); `Reciever` is untouched — the type is still spelled that way in code, while the docs use the correct **Receiver**. Prefer correct spellings in new code and documentation, and finish any rename across the whole solution in one go rather than leaving it half-applied.

**Known inconsistency to be aware of:** `ManifestStatus` in code (`Manifest.cs`) defines 6 states (Proposed, Confirmed, Prepared, Transit, Arrived, Lost) via instance properties returning `new` instances (an unusual pattern — worth flagging rather than copying if extending), while `docs/manifest-status.puml` documents a fuller 10-state flow (adds Created, Rejected, Ready, Delivered, Returned) with different transitions. Treat the `.puml` as the intended target design and the code as not yet caught up — check with the user before assuming either is authoritative.

### Target hosting architecture (Azure)

Not yet provisioned on Azure, but **simulated locally in `iac/`** — see `iac/README.md`. `docker compose up -d --wait` starts stand-ins for every container on the diagram (Traefik for Cloudflare, Azurite for Blob/Queue Storage, SQL Server for Azure SQL, Keycloak for Entra External ID, WireMock for the HMRC APIs, Grafana OTEL-LGTM for App Insights, Mailpit for ACS); `tofu apply` then provisions resources into them the way `azurerm` would. Use it before changing anything here — several of the gotchas below are exercised by it.

The local Keycloak realm (`iac/tofu/keycloak.tf`) seeds **three** logins, all with password `password`: `admin` (Administrator), `operator` (Dispatcher + Loader + Purchaser), and `groundofficer` (GroundOfficer). Locally each app role is delegated through a Keycloak **group** of the same name and users get roles by group membership — this is the group-assignment model Entra keeps behind a paid tier, used here only because it keeps the seed logins readable; the token still carries a flat `roles` claim, so authorisation policies port to Azure app roles unchanged. `groundofficer` is deliberately kept as its own isolated login so the receiver-address segregation that role carries in production holds locally too — no other seed user can resolve a receiver address. Keycloak runs with `KC_HOSTNAME_BACKCHANNEL_DYNAMIC: "true"` (`iac/local/docker-compose.yml`) so the discovery document's `jwks_uri`/`token_endpoint` follow the request host: the app container fetches signing keys over the compose network (`keycloak:8080`) while the token `iss` stays the browser-facing `localhost:8081`. Without it, in-container token validation fails because the app is told to fetch keys from a `localhost` URL it cannot reach.

The full reasoning, cost model and security posture live in **`docs/recommendations.md`**; read it before making infrastructure or hosting changes, because most of these choices are load-bearing rather than incidental.

The binding constraints:

- **Permanent Azure free allowances only.** No trial credits, no introductory offers. Anything with a fixed monthly charge is out — this is why the design uses Cloudflare Free at the edge rather than Azure Front Door.
- **Convoys run roughly once a month**, so load is long quiet periods punctuated by a burst of a few days. Everything scales to zero between convoys, and cold starts are an accepted trade.
- **UK South**, for UK data residency over volunteer and donor personal data.

Decisions already taken:

| Concern | Choice |
| --- | --- |
| Web UI + API | **One deployable** on Azure Container Apps (Consumption). Do not split them without a reason — the split doubles compute and adds an auth boundary. |
| Background work | Azure Functions (Consumption), queue- and timer-triggered |
| Database | Azure SQL free offer (serverless, auto-pause) |
| Documents | One Blob Storage account, `manifests/` `gmr/` `elo/` prefixes — not one account per document type |
| Identity | Microsoft Entra External ID, app roles (not group assignment, which needs a paid tier). The local Keycloak sim does use role groups — a readability convenience, not a design change; see above. |
| Edge | Cloudflare Free — DNS, TLS, CDN, WAF |
| CI/CD | GitHub Actions → ghcr.io, authenticated by OIDC federated credential |

Two gotchas that will cost you a debugging session if missed:

- **Persist ASP.NET Core data-protection keys** to Blob Storage, encrypted with a Key Vault key. Container Apps replicas are ephemeral, so the default in-container key ring silently logs users out after every scale-to-zero. See `docs/recommendations.md` §3.2.
- **The Azure SQL free offer's auto-pause delay has a 60-minute minimum**, so scattered short bursts of activity burn the monthly compute allowance far faster than their duration suggests. See §2.3.

Security principles this design commits to: managed identity everywhere (shared-key auth *disabled* on storage, Entra-only auth on SQL), pull-based HMRC integration rather than an inbound webhook, short-lived user-delegation SAS for documents, and segregated storage for Ukrainian delivery detail. That last one matters most — see the note under Docs below.

### Docs

- `docs/domain/key-concepts.md` — the shared vocabulary (roles, Convoy, Manifest, Box, Receiver, documents). **Start here** for domain questions; it is more current than the code.
- `docs/recommendations.md` — Azure hosting design, cost model, security recommendations, and open questions still needing a decision.
- `docs/c4/1-system-context.puml` — C4 level 1. Person-to-person and person-to-third-party interactions belong here, not on level 2.
- `docs/c4/2-containers.puml` — C4 level 2, the target Azure deployment. Uses C4-PlantUML plus Azure-PlantUML sprites from the `release/2-2` dist; verify a sprite exists in that release before adding an include, as newer Azure services are missing from it.
- `docs/process.puml` — activity diagram of the end-to-end manifest creation process (admin approval → GMR/ELO generation → box prep → loading → departure → stock updates).
- `docs/manifest-status.puml` — state diagram for manifest status (see inconsistency note above).
- `docs/schemas/hmrc/` — OpenAPI specs for the HMRC Goods Movement System Haulier API and Push Pull Notifications API.
- `iac/README.md` — how to run the local Azure simulation, what it stands in for, and the five ways it deliberately differs from the target design. **`iac/local/`** is the docker compose substrate, **`iac/tofu/`** the OpenTofu control plane that provisions into it.

**Data sensitivity — read before touching `Reciever`, `Address` or manifest generation.** Ukrainian delivery addresses and receiver contacts are the highest-risk data in the system: a manifest listing precise addresses is a targeting document, and it crosses several borders where it may be inspected or seized. The design segregates that data behind the Ground Officer role, redacts it from anything that travels, and audits every read. Do not widen access to it, add it to a printed document, or log it, without checking with the user first. See `docs/domain/key-concepts.md` § Data Sensitivity.

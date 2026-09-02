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

The solution builds clean, with **zero warnings** — the Domain `CS8618` nullability warnings were cleared by the domain remediation (`required` on genuinely required scalars, nullable navigation properties). Keep it at zero: a warning that is allowed to persist is one nobody reads.

## Architecture

Solution follows a layered structure under `src/`:

- **`UA.Action.Freedom.Domain`** — plain C# domain model, no framework dependencies. This is where the business concepts live (see below).
- **`UA.Action.Freedom.Application`** — use cases / orchestration. Hand-rolled CQRS: `Abstractions/ICommandHandler<,>` + `IQueryHandler<,>`, one handler per use case, all registered explicitly in `AddFreedomApplication()` (no MediatR, no assembly scanning). Slices so far: `Vehicles/`, `People/`, `Convoys/`, `Receivers/`, `Boxes/`, `Manifests/` — each with create/update/delete commands and get/list queries behind its own `I*Repository` port. Handlers return small outcome enums (`Created`/`Conflict`, `Updated`/`NotFound`) so the API maps status codes without exceptions; where creation cannot conflict the handler returns the new identifier instead (`People` mints a `Guid`, `Convoys` returns the `IDENTITY` the insert assigned). Each slice's `*ReadModel` is a flat read + write shape whose CLR types line up with the column types — a deliberate CQRS projection, not a workaround: `Domain` is hydratable since the remediation, but Dapper maps rows, not object graphs. Paging is clamped in the list handler (1..200, default 50), never in the endpoint.
- **`UA.Action.Freedom.Data`** — persistence. `SqlConnectionFactory` (`IDbConnectionFactory`) builds `SqlConnection`s from `ConnectionStrings:Freedom`, same key as the health check. One Dapper repository per slice — `Vehicles/`, `People/`, `Convoys/`, `Receivers/`, `Boxes/`, `Manifests/` — all registered by `AddFreedomData()`. **There are two connection factories, and the difference is a security boundary.** `IDbConnectionFactory` → `ConnectionStrings:Freedom`, the application's own identity (`freedom_app`), which is `DENY SELECT`'d on the `sensitive` schema. `ISensitiveDbConnectionFactory` → `ConnectionStrings:FreedomSensitive`, a principal in the `ground_officer` role, and the only way to read a Ukrainian delivery address. `ReceiverDetailRepository` is the one class that takes the second — a separate interface rather than a named lookup, so a repository asking for the ordinary factory *cannot* be handed the Ground Officer connection, and widening that needs a constructor change a reviewer will see. **Dapper's constructor-mapping is strict**: `dbo.Vehicle` uses `int` (not `tinyint`/`smallint`) for the enum and year columns so they line up with the CLR types of `VehicleReadModel`'s primary constructor, and the same rule governs every new table. `ConvoyRepository.ReplaceRouteAsync` holds the only transaction in the codebase: a route is meaningful only as a whole journey, so deleting the old stops and half-inserting the new ones must not be possible. The schema lives in `iac/local/sql/001-schemas.sql` (hand-written idempotent T-SQL, no migration tool) and is applied by `iac/tofu/database.tf`, which re-runs it whenever the file's hash changes.
- **`UA.Action.Freedom.Api`** — ASP.NET Core minimal API host (`Program.cs`). `/vehicles` CRUD (`Vehicles/VehicleEndpoints.cs`, `MapFreedomVehicles()`) — natural key on VIN, so the route is `/vehicles/{vin}`. `/people` CRUD (`MapFreedomPeople()`), server-minted `Guid` key, `?driversOnly=true` for the dispatcher's shortlist. `/convoys` CRUD (`MapFreedomConvoys()`) plus its sub-resources: `PUT/GET /convoys/{id}/route` (replaced whole, renumbered 1..n in list order), `PUT|DELETE /convoys/{id}/vehicles/{vin}`, and `POST /convoys/{id}/publish-truck-list`. `/receivers` CRUD (`MapFreedomReceivers()`) returning reference/organisation/region only, plus `GET|PUT /receivers/{ref}/detail` for the delivery address — Ground Officer alone, and every resolve is audited. `/boxes` CRUD (`MapFreedomBoxes()`) plus `GET|POST|DELETE /boxes/{id}/items` and `POST /boxes/{id}/validate`. `/manifests` CRUD (`MapFreedomManifests()`) plus `GET|PUT /manifests/{id}/teams/{leg}`, `GET|PUT|DELETE /manifests/{id}/boxes/{boxId}`, `GET /manifests/{id}/weight`, and one `POST` per edge of the state diagram (`propose`, `approve`, `reject`, `prepare`, `ready`, `depart`, `deliver`, `lose`, `return`). JWT bearer auth (`Configuration/AuthenticationExtensions.cs`, `AddFreedomAuthentication`/`AddFreedomAuthorization`) driven by `OidcOptions`: `RoleClaimType = "roles"`, `MapInboundClaims = false` (Keycloak/Entra send a flat `roles` claim — without this it gets rewritten to the WS-Fed role URI and every policy fails), `RequireHttpsMetadata` off in Development, audience checked only when `Oidc:Audience` is set (Keycloak issues `aud: account`). Policies: `vehicles:read` / `people:read` / `convoys:read` (Administrator/Purchaser/Dispatcher/Loader), `vehicles:write` (Administrator/Purchaser), `people:write` (Administrator only — approving volunteers is the Administrator's job), `convoys:write` (Administrator/Dispatcher), `receivers:read` (all operational **plus** GroundOfficer), `receivers:write` (Administrator/GroundOfficer), and `receivers:detail` (**GroundOfficer alone** — the narrowest policy in the API; deleting a receiver also sits behind it, because that removes an address), `boxes:read` (all operational), `boxes:write` (Administrator/Dispatcher/Loader) and `boxes:validate` (Administrator/Loader — packing a box and vouching for what is in it are different acts), `manifests:read` (all operational), `manifests:write` (Administrator/Dispatcher) and `manifests:approve` (**Administrator only** — approval releases the GMR and freezes the manifest, so the person who builds one is not the person who signs it off). GroundOfficer is excluded from every other policy, and the isolation runs both ways. Request bodies validated by FluentValidation via `Configuration/ValidationFilter<T>`; errors are `ProblemDetails` (`AddProblemDetails()` + `UseExceptionHandler()`). Also real configuration (`Configuration/`, environment-variable only), `/health/live` + `/health/ready` probes that genuinely check SQL, blob, queue and OIDC discovery (`Health/`), and data-protection keys persisted to blob storage. OpenTelemetry traces, metrics and logs are wired in `Installer/TelemetryInstaller.cs` (`builder.AddFreedomTelemetry()`): ASP.NET Core + HttpClient + runtime instrumentation, exported over OTLP to whatever `OTEL_EXPORTER_OTLP_ENDPOINT` names (the Grafana OTEL-LGTM container locally, App Insights in Azure). The exporter is skipped when that variable is unset, so `dotnet run` and the in-memory component tests collect but do not ship. References `FluentValidation` for future use.
- **`UA.Action.Freedom.CustomsWorker`** — the Azure Function of the C4 diagram, as a plain `BackgroundService` so it runs anywhere. `GmrSubmissionProcessor` drains the customs work queue and submits to HMRC; `GmrOutcomeCollector` polls the Push Pull Notifications box for outcomes (pull only — no inbound endpoint, per `recommendations.md` §4.1) and writes GMR documents to blob storage. Storage and queue access sit behind ports (`ICustomsWorkQueue`, `IGmrDocumentStore`) so the rules about *when a queue message is deleted* — the part that loses a convoy's GMR if wrong — are unit-testable without a storage account.
- **`UA.Action.Freedom.ManifestWorker`** — renders the document that travels with a vehicle. `ManifestDocumentProcessor` drains the `manifest-documents` queue and writes to the `manifests/` blob prefix, with the same three-way queue disposition as the Customs Worker (complete / dead-letter / leave alone). **It has no database access at all**, and that is the design: the Freedom Application composes the whole document and puts it on the queue, so the worker cannot read a Ukrainian delivery address because it cannot read anything. `ManifestDocumentRequest` has nowhere to carry a street, contact or phone — the redaction is the type, not a rule someone has to remember (see the data-sensitivity note below). Output is plain text on purpose: deterministic and testable, and a PDF with letterhead can wrap it later without changing what is on it.

- **`HMRC.GVMS`** (`src/HMRC.GVMS`) / **`HMRC.PushPullNotifications`** (`src/HMRC.PushPullNotifications`) — NSwag-generated typed HTTP clients for the HMRC Goods Vehicle Movements and Push Pull Notifications APIs. These are standalone HMRC SDKs and deliberately carry no `UA.Action.Freedom` prefix — namespace and assembly name match the project name. The `Generated/` folder is committed codegen output; regenerate with `build/nswag/regenerate.ps1 -Api <goods-vehicle-movements|push-pull-notifications>` and see `build/nswag/README.md`. Each project adds a hand-written `*ClientOptions` + `Add*Client(this IServiceCollection)` DI extension (typed `HttpClient`, HMRC versioned `Accept` header, caller supplies the OAuth handler). Do not hand-edit `Generated/`. Their unit tests live in `tests/HMRC.GVMS.Tests.Unit` and `tests/HMRC.PushPullNotifications.Tests.Unit` — one dedicated test project per SDK, not folded into `UA.Action.Freedom.Tests.Unit`.

  > **Known bug: `HMRC.PushPullNotifications` cannot deserialise a notification.** HMRC sends `"messageContentType": "application/json"`; NSwag generated the enum with `[EnumMember(Value = "application/json")]` but decorated the property with `JsonStringEnumConverter<MessageContentType>`, which matches C# member names (`Application_json`) and ignores `[EnumMember]`. Every response throws `JsonException`. This affects real HMRC, not just the local stub, so do not "fix" it by changing the stub. The fix belongs in `build/nswag/` plus a regeneration. Reproduce with the local environment — `iac/README.md` § Known issues.

Seven test projects under `tests/`. `HMRC.GVMS.Tests.Unit` and `HMRC.PushPullNotifications.Tests.Unit` hold the SDK client tests. The four `UA.Action.Freedom.Tests.*` projects each cover every slice the same way, so adding one means adding a file in each:

- **`Unit`** — handler tests with NSubstitute against the slice's `I*Repository` port, plus both workers' processors. A `*TestData` factory per slice with every parameter defaulted; a fresh substitute per test, no shared fixture.
- **`Component`** — the API in memory via `WebApplicationFactory<Program>`, with an `InMemory*Repository` and the `TestAuthHandler` scheme swapped in by a `FreedomApi.With*(...)` helper. Assertions read `JsonElement`, never the read model, so the JSON contract is what is pinned. This is where the authorization split for each slice is proved.
- **`Integration`** — the real Dapper repositories against real SQL, `[Trait("Category","Integration")]`, `ConnectOrSkipAsync` probing the *table* and calling `Assert.Skip` when it is not there. **The assembly is `[assembly: Parallelization(Mode = ParallelMode.None)]`** — these share one database and the foreign keys between Vehicle, Convoy and Manifest made parallel classes deadlock each other about one run in four. Do not re-enable it.
- **`BDD`** — **Reqnroll** (`Reqnroll.xunit.v3`), one `.feature` per slice driving HTTP against the **deployed containers** (edge on `http://localhost:8080`, Keycloak on `http://localhost:8081`, overridable by `FREEDOM_*` env vars) with real password-grant tokens. `Background` calls `Given the Freedom API exposes "/route"`, which `Assert.SkipUnless`es the feature and distinguishes "the stack is down" from "the running image predates this feature". Generic HTTP steps live in `ApiSteps`; Reqnroll matches step text globally, so a duplicate in a per-slice class is an ambiguous binding, not an override. After a code change, rebuild the image (`cd iac/local && docker compose build app manifest-worker && docker compose up -d --wait app edge manifest-worker`) before running it.

**CI:** `.github/workflows/build-and-test.yml` runs on `push`/`pull_request` to `main` **only when a code path changes** — a `paths:` filter of `src/`, `tests/`, `iac/`, `web/`, `build/`, the root `UA.Action.Freedom.slnx`/`global.json`/`GitVersion.yml`, or the workflow file. Doc-only commits (`docs/**`, `plans/**`, `*.md`) do not trigger it; `workflow_dispatch` forces a run. It has five jobs. `build-and-test` runs `dotnet test --solution` (Integration + BDD self-skip there — no infrastructure) and emits the version outputs. `frontend` runs the `web/` gates (typecheck/lint/format/test/build). `acceptance` stands up the `iac/` simulation (`docker compose` substrate + `tofu apply` for realm/storage/schema), **builds the three service container images** (`ua-action-freedom-{api,customs-worker,manifest-worker}`), runs Integration + BDD + Playwright against *those exact images*, and on a non-PR run pushes them to `ghcr.io/thelastcolonial/*` (`<semver>`/`-beta.<run>`, `sha-<short>`, `latest` on main) with build-provenance attestations — building here rather than in a separate job is what makes the tested image the published one. `nuget` packs and pushes **only** `HMRC.GVMS` + `HMRC.PushPullNotifications` to GitHub Packages (`-beta.<run>` on non-fork PRs, full on main). `release` (main push + `isRelease` only) cuts a GitHub Release annotated with the pushed image digests — no file assets. `nuget` and `release` gate behind `acceptance`, so a failing e2e blocks both. `iac/local/docker-compose.yml` is unchanged; CI runs it with `--no-build` because the images are pre-built and loaded.

### README.md maintenance

The **README.md is the primary onboarding document** for new developers. Keep it synchronized with the codebase — when code changes, the README changes in the same commit or PR. Update these sections immediately when:

- **Prerequisites** — Add/remove .NET versions, Docker, OpenTofu, or any required tools
- **Project Structure** — When adding/removing projects or slices (e.g., new `UA.Action.Freedom.SomethingWorker` or test project); update both the `src/` and `tests/` trees
- **Usage commands** — If build, test, or run commands change (e.g., new solution structure, test runner opts); verify command output locally before committing
- **API Endpoints** — When adding new REST routes or sub-resources (e.g., `PUT /vehicles/{vin}/service-record`), document the route, HTTP method, and access policy; update both the listing and the policy matrix if applicable
- **Architecture** — When introducing new patterns (new repository port, new worker, new state machine, new security boundary), add a short paragraph to the Architecture section explaining the pattern
- **Local setup** — If Docker Compose services, environment variables, Keycloak roles, or OpenTofu provisioning steps change, update the "Local Development Environment" section; test the full stack locally first
- **Development guidance** — When the workflow for adding a slice changes (new test file locations, new handler patterns, new schema table conventions), update the "Adding a New Slice" subsection
- **Known Issues** — When known gotchas are resolved, remove them; when new ones discovered, add them and link to `docs/gotchas-and-open-questions.md`

**These updates are part of the feature work, not follow-up cleanup** — they go in the same commit as the code change, and any PR that touches code but not the README is incomplete.

### Domain model (`src/UA.Action.Freedom.Domain`)

Core entities and how they relate:

- **`Person`** — base type for any individual (name, DOB, join date, phone). `Driver : Person` adds `Convoys` and `Committed`. `Receiver` (the delivery contact) carries an `Organisation`, a region, and — for a Ground Officer only — an `Address` and a responsible `Person`.
- **`Vehicle`** — a donated vehicle (VIN, plate, weight, fuel/transmission type, purchaser, drivers, and its `Convoy`). Vehicles are themselves part of the aid — they are handed over in Ukraine, not driven back.
- **`Convoy`** — a group of `Vehicle`s travelling together, with a `Route` (ordered list of `Address` stops) and start/expected-end timestamps.
- **`DriverTeam`** — a `PrimaryDriver` + `SecondaryDriver` pair. A `Manifest` has two teams: `DriverUK` (UK→Europe leg) and `DriverBorder` (Europe→Ukraine leg).
- **`Box`** — a packed container of `Item`s with a confirmed weight, validation state (`ValidatedBy`/`ValidatedAt`), current `Location`, and target `Receiver`.
- **`Manifest`** — the unit that ties a `Vehicle`, both `DriverTeam`s, and the `Box[]` cargo together for one convoy leg. `TotalWeightKg()` sums vehicle + cargo + a fixed 200kg (2 drivers + bags) + 45kg fuel allowance — this constant-padding convention is intentional (border-check estimate), not a bug.

**Spelling conventions:** the misspelled type names have all been corrected — `Veichle` → `Vehicle`, `Reciever` → `Receiver`, `Person.Joinned` → `Joined`, `ResponsibleIndiviual` → `ResponsibleIndividual`. Nothing is half-applied any more; keep it that way, and finish any future rename across the whole solution in one go.

**Identity is deliberately not standardised.** Each entity carries the id type that matches how it is actually referenced: `Vehicle` has no id (VIN is the natural key and the route segment), `ConvoyId(int)`, `BoxId(int)`, `DriverTeamId(int)`, `ManifestId(string)`, `PersonId(Guid)`, `ReceiverRef(Guid)`, and `Item` a raw `Guid`. The two `Guid`s are not enumerable on purpose — they identify volunteer personal data and Ukrainian receivers. Do not "harmonise" these.

**`ManifestStatus` is the 10-state model** from `docs/manifest-status.puml`, which `docs/recommendations.md` §5.3 asked to be confirmed as authoritative — it now is, and the code matches it. The old 6-state `record` with instance properties is gone. The edges live as data in `ManifestTransitions.CanTransition(from, to)` (`Manifest.cs`), pinned edge-by-edge by `tests/UA.Action.Freedom.Tests.Unit/Domain/ManifestTransitionsTests.cs`. The happy path is linear; the only backward edge is `Rejected → Proposed`. Nothing may reopen a confirmed manifest, because confirmation is what releases it to GMR submission — and once `Manifest.GmrSubmittedAt` is set the manifest is frozen entirely (§5.2).

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
- `docs/local-authentication.md` — how to get a token locally, the three seed logins and what each may do, the full role/policy matrix, and why `MapInboundClaims = false` matters.
- `docs/gotchas-and-open-questions.md` — **read this before a long debugging session.** Every trap found while building the slices out: the tooling ones (MTP verbs, `QUOTED_IDENTIFIER` under sqlcmd, the integration-test deadlock), the security invariants that are easy to remove by accident, the domain rules that look like bugs, and what is still undecided.
- `docs/recommendations.md` — Azure hosting design, cost model, security recommendations, and open questions still needing a decision.
- `docs/c4/1-system-context.puml` — C4 level 1. Person-to-person and person-to-third-party interactions belong here, not on level 2.
- `docs/c4/2-containers.puml` — C4 level 2, the target Azure deployment. Uses C4-PlantUML plus Azure-PlantUML sprites from the `release/2-2` dist; verify a sprite exists in that release before adding an include, as newer Azure services are missing from it.
- `docs/process.puml` — activity diagram of the end-to-end manifest creation process (admin approval → GMR/ELO generation → box prep → loading → departure → stock updates).
- `docs/manifest-status.puml` — state diagram for manifest status (see inconsistency note above).
- `docs/schemas/hmrc/` — OpenAPI specs for the HMRC Goods Movement System Haulier API and Push Pull Notifications API.
- `iac/README.md` — how to run the local Azure simulation, what it stands in for, and the five ways it deliberately differs from the target design. **`iac/local/`** is the docker compose substrate, **`iac/tofu/`** the OpenTofu control plane that provisions into it.

**The receiver segregation is built and enforced — do not weaken it.** Three independent controls, and each one holds if the others are removed by mistake:

1. **The `receivers:detail` policy** — GroundOfficer alone (`Configuration/AuthenticationExtensions.cs`).
2. **A separate database identity** — `ISensitiveDbConnectionFactory` / `ConnectionStrings:FreedomSensitive`, used only by `ReceiverDetailRepository`.
3. **`DENY SELECT ON SCHEMA::sensitive TO freedom_app`** — the application's own identity physically cannot read a delivery address. `tests/UA.Action.Freedom.Tests.Integration/Receivers/ReceiverSegregationTests.cs` asserts this against the real database, and it only means anything because the app connects as `freedom_app` rather than `sa` (sysadmin bypasses permission checks — that switch is why `iac/local/.env.example` carries `FREEDOM_APP_DB_PASSWORD`).

`ReceiverReadModel` has no address or contact fields at all, so code holding one has nothing sensitive to leak — that is what makes redaction structural in document generation and logging rather than a rule someone has to remember. `ReceiverDetailRepository.ResolveAsync` writes the audit row *in the same transaction* as the read, and logs the attempt even when nothing is found; the trail outlives the address it describes.

**The manifest lifecycle, and what "frozen" actually means.** Every state change is its own `POST` (one per edge of `manifest-status.puml`), never a `PATCH` of a status field: most pairs of states are unconnected, and two of the rules have nothing to do with the pair involved. `TransitionManifestHandler` delegates legality to `ManifestTransitions.CanTransition` and adds the two rules the diagram cannot express — a manifest may only be **proposed** against a convoy whose truck list is published (`process.puml`), and a frozen manifest may not be **reopened**.

Note the precision on the freeze: §5.2 forbids *edits*, not *progress*. `Manifest.GmrSubmittedAt` blocks `PUT`, team assignment, cargo changes and delete — but `Preparing → Ready → InTransit → Delivered` still run, because those report what happened to a load HMRC already knows about. Blocking them would strand every approved manifest in `Confirmed` for ever. Only `Proposed`/`Rejected` are refused when frozen, and that is a guard against a future backward edge rather than a path anything takes today.

`POST /manifests/{id}/approve` is the fork in `process.puml`: `ConfirmAndFreezeAsync` sets status and the GMR stamp in **one** statement (a manifest that is Confirmed but not yet frozen is editable, and that window is the thing §5.2 rules out), and only then is the submission enqueued. That order is load-bearing — a failed enqueue leaves a frozen manifest with no GMR, which is visible and retryable; the reverse risks an editable manifest whose GMR is already on its way.

**Three write-once records, all enforced the same way.** `Convoy.TruckListPublishedAt`, `Box.ValidatedAt`/`ValidatedByPersonId` and `Manifest.Status`/`GmrSubmittedAt` are stamped by a dedicated `POST` transition and are *absent from the corresponding `UPDATE` statement and request body*, so no ordinary edit can set, clear or forge them. Each transition's SQL is conditional (`WHERE ... AND TruckListPublishedAt IS NULL` / `AND ValidatedAt IS NULL` / `AND Status = @from`), so the database settles a race rather than a read-then-write in C#. Both then freeze their aggregate: a published truck list will not take or release a vehicle, and a validated box will not take or release an item or change receiver — and a frozen manifest will not be edited — in each case because a manifest or a border document would otherwise be describing something that is no longer true.

**Data sensitivity — read before touching `Receiver`, `Address` or manifest generation.** Ukrainian delivery addresses and receiver contacts are the highest-risk data in the system: a manifest listing precise addresses is a targeting document, and it crosses several borders where it may be inspected or seized. The design segregates that data behind the Ground Officer role, redacts it from anything that travels, and audits every read. Do not widen access to it, add it to a printed document, or log it, without checking with the user first. See `docs/domain/key-concepts.md` § Data Sensitivity.

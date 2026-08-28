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
dotnet run --project src/UA.Action.Freedom.Api/UA.Action.Freedom.Api.csproj          # run the API (http://localhost:5100)
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

> **The Domain project currently does not build.** The `Veichle` → `Vehicle` rename (commit `ab54ace`) was left incomplete: `Manifest.cs:16` and `Convoy.cs:13` still reference the old type name and fail with `CS0246`. Finishing the rename in those two places is the fix. Expect `dotnet build` and `dotnet test` to fail until then — this is a known state, not something you have just broken.

## Architecture

Solution follows a layered structure under `src/`:

- **`UA.Action.Freedom.Domain`** — plain C# domain model, no framework dependencies. This is where the business concepts live (see below). Currently the only project with real content.
- **`UA.Action.Freedom.Application`** — intended for use cases/orchestration logic; currently just a scaffold (`Class1.cs`).
- **`UA.Action.Freedom.Data`** — intended for persistence, references `Dapper`; currently just a scaffold (`Class1.cs`).
- **`UA.Action.Freedom.Api`** — ASP.NET Core minimal API host (`Program.cs`). Currently only has the default template's `/weatherforecast` endpoint — no domain endpoints wired up yet. References `FluentValidation` and `OpenTelemetry.Api` for future use.
- **`HMRC.GVMS`** (`src/HMRC.GVMS`) / **`HMRC.PushPullNotifications`** (`src/HMRC.PushPullNotifications`) — NSwag-generated typed HTTP clients for the HMRC Goods Vehicle Movements and Push Pull Notifications APIs. These are standalone HMRC SDKs and deliberately carry no `UA.Action.Freedom` prefix — namespace and assembly name match the project name. The `Generated/` folder is committed codegen output; regenerate with `build/nswag/regenerate.ps1 -Api <goods-vehicle-movements|push-pull-notifications>` and see `build/nswag/README.md`. Each project adds a hand-written `*ClientOptions` + `Add*Client(this IServiceCollection)` DI extension (typed `HttpClient`, HMRC versioned `Accept` header, caller supplies the OAuth handler). Do not hand-edit `Generated/`. Their unit tests live in `tests/HMRC.GVMS.Tests.Unit` and `tests/HMRC.PushPullNotifications.Tests.Unit` — one dedicated test project per SDK, not folded into `UA.Action.Freedom.Tests.Unit`.

Six test projects under `tests/`: `HMRC.GVMS.Tests.Unit` and `HMRC.PushPullNotifications.Tests.Unit` hold the SDK client tests; the four `UA.Action.Freedom.Tests.*` projects (`Unit`, `Component`, `Integration`, `BDD`) mirror the app's test types but still contain only the default `UnitTest1.cs` stub — no real domain tests exist yet to pattern-match against.

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

Designed but not yet built — `iac/` is still empty. The full reasoning, cost model and security posture live in **`docs/recommendations.md`**; read it before making infrastructure or hosting changes, because most of these choices are load-bearing rather than incidental.

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
| Identity | Microsoft Entra External ID, app roles (not group assignment, which needs a paid tier) |
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
- `iac/` — placeholder for infrastructure-as-code; nothing provisioned yet.

**Data sensitivity — read before touching `Reciever`, `Address` or manifest generation.** Ukrainian delivery addresses and receiver contacts are the highest-risk data in the system: a manifest listing precise addresses is a targeting document, and it crosses several borders where it may be inspected or seized. The design segregates that data behind the Ground Officer role, redacts it from anything that travels, and audits every read. Do not widen access to it, add it to a printed document, or log it, without checking with the user first. See `docs/domain/key-concepts.md` § Data Sensitivity.

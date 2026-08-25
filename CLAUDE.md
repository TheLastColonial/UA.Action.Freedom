# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

UA.Action.Freedom — automation to support Ukrainian Action, a charity that runs supply convoys (donated vehicles + cargo) from the UK to Ukraine. The domain models the lifecycle of preparing a convoy: sourcing vehicles, packing boxes of items, assigning driver teams, building a manifest per vehicle, and tracking the convoy's route and status through to delivery.

## Commands

Built on .NET 10 (`net10.0`), solution file is `UA.Action.Freedom.slnx`.

```
dotnet build UA.Action.Freedom.slnx          # build everything
dotnet test UA.Action.Freedom.slnx           # run all test projects
dotnet test tests/UA.Action.Freedom.Tests.Unit/UA.Action.Freedom.Tests.Unit.csproj   # run one test project
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"                       # run a single test
dotnet run --project src/UA.Action.Freedom.Api/UA.Action.Freedom.Api.csproj          # run the API (http://localhost:5100)
```

Test framework is xUnit v3 with `AwesomeAssertions` (fluent assertions), `NSubstitute` (mocking), and `MELT` (testable `ILogger`). `Xunit` is globally usable via implicit `<Using>` in each test csproj — no `using Xunit;` needed.

## Architecture

Solution follows a layered structure under `src/`:

- **`UA.Action.Freedom.Domain`** — plain C# domain model, no framework dependencies. This is where the business concepts live (see below). Currently the only project with real content.
- **`UA.Action.Freedom.Application`** — intended for use cases/orchestration logic; currently just a scaffold (`Class1.cs`).
- **`UA.Action.Freedom.Data`** — intended for persistence, references `Dapper`; currently just a scaffold (`Class1.cs`).
- **`UA.Action.Freedom.Api`** — ASP.NET Core minimal API host (`Program.cs`). Currently only has the default template's `/weatherforecast` endpoint — no domain endpoints wired up yet. References `FluentValidation` and `OpenTelemetry.Api` for future use.

Four test projects under `tests/` mirror different test types (`Unit`, `Component`, `Integration`, `Tests.BDD`) but each currently contains only the default `UnitTest1.cs` stub — no real tests exist yet to pattern-match against.

### Domain model (`src/UA.Action.Freedom.Domain`)

Core entities and how they relate:

- **`Person`** — base type for any individual (name, DOB, join date, phone). `Driver : Person` adds `Convoys` and `Committed`. `Reciever` (the delivery contact) wraps a `Person` with an `Address` and `Organisation`.
- **`Veichle`** — a donated vehicle (VIN, plate, weight, fuel/transmission type, purchaser, drivers, and its `Convoy`). Note the project-wide spelling `Veichle`/`Veichles` (not "Vehicle") — match it for consistency rather than "fixing" it in isolation.
- **`Convoy`** — a group of `Veichle`s traveling together, with a `Route` (ordered list of `Address` stops) and start/expected-end timestamps.
- **`DriverTeam`** — a `PrimaryDriver` + `SecondaryDriver` pair. A `Manifest` has two teams: `DriverUK` (UK→Europe leg) and `DriverBorder` (Europe→Ukraine leg).
- **`Box`** — a packed container of `Item`s with a confirmed weight, validation state (`ValidatedBy`/`ValidatedAt`), current `Location`, and target `Reciever`.
- **`Manifest`** — the unit that ties a `Veichle`, both `DriverTeam`s, and the `Box[]` cargo together for one convoy leg. `TotalWeightKg()` sums vehicle + cargo + a fixed 200kg (2 drivers + bags) + 45kg fuel allowance — this constant-padding convention is intentional (border-check estimate), not a bug.

**Known inconsistency to be aware of:** `ManifestStatus` in code (`Manifest.cs`) defines 6 states (Proposed, Confirmed, Prepared, Transit, Arrived, Lost) via instance properties returning `new` instances (an unusual pattern — worth flagging rather than copying if extending), while `docs/manifest-status.puml` documents a fuller 10-state flow (adds Created, Rejected, Ready, Delivered, Returned) with different transitions. Treat the `.puml` as the intended target design and the code as not yet caught up — check with the user before assuming either is authoritative.

### Docs

- `docs/process.puml` — PlantUML activity diagram of the end-to-end manifest creation process (admin approval → GMR/ELO generation → box prep → loading → departure → stock updates).
- `docs/manifest-status.puml` — PlantUML state diagram for manifest status (see inconsistency note above).
- `docs/domain/` — currently empty (placeholder).
- `iac/` — currently empty (placeholder for infrastructure-as-code).

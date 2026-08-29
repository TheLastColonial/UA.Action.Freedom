# Complete the Freedom API and workers across the remaining domain

## Context

`UA.Action.Freedom` has exactly one vertical slice wired end to end: **Vehicles**.
`Domain.Vehicle` → `Application/Vehicles/*` (hand-rolled CQRS behind `IVehicleRepository`) →
`Data/Vehicles/VehicleRepository` (Dapper over `dbo.Vehicle`) → `Api/Vehicles/VehicleEndpoints`
(`/vehicles/{vin}`, FluentValidation, `vehicles:read` / `vehicles:write`), with unit, component,
integration and BDD tests at each level. Every other domain concept — Convoy, Manifest, Box, Item,
Person/Driver, DriverTeam, Receiver — is a POCO in `src/UA.Action.Freedom.Domain` with no
persistence, no use cases and no HTTP surface. The `CustomsWorker` is the only background service.

This plan finishes the job: the same vertical slice for every remaining model, the manifest
lifecycle the process diagrams describe, and two new background processors.

**Decisions taken with the user before planning:**

| Question | Decision |
| --- | --- |
| Scope | Everything remaining |
| Domain model | Fix it first, before any new slice |
| Depth | CRUD **plus** lifecycle transitions |
| New background work | Manifest document generation **and** notifications |
| `ManifestStatus` (recommendations §5.3) | The **10-state `manifest-status.puml` is authoritative** |
| Sensitive receiver reads | Second connection string bound to the `ground_officer` DB role; the `DENY` stays |
| Identity | **No standardisation** — each entity keeps the id type that matches it |
| Renames | All of them, in one commit, before any new slice |

### What "fix the domain first" means here — and what it does not

`Domain.Vehicle` cannot be hydrated from a row: no `Id`, and non-nullable `Convoy`/`Purchaser`
navigation. That defect is what forced `VehicleReadModel` into existence, and repeating the
workaround eight more times would entrench it.

The fix is to make the domain **correct and hydratable**, and to give it the behaviour it is
missing (a real status model, a transition guard). It is **not** a move to hydrating object graphs
through Dapper. Flat per-slice read models stay — after this change they are a deliberate CQRS
projection rather than a workaround, and the XML doc on each says so. Concretely:

- Domain gains identity, correct nullability, a usable `ManifestStatus` and the invariants.
- Repositories keep returning flat `*ReadModel` records whose CLR types line up with column types.
- The existing Vehicle slice is touched only for renames and the doc-comment correction.

---

## Increment 1 — Domain remediation

`src/UA.Action.Freedom.Domain/*`. No new endpoints; the solution must build and all existing
tests pass unchanged at the end.

**Renames** (one commit, whole solution — CLAUDE.md forbids leaving a rename half-applied):

| From | To |
| --- | --- |
| `Reciever` (type, file, every usage) | `Receiver` |
| `Person.Joinned` | `Person.Joined` |
| `Receiver.ResponsibleIndiviual` | `Receiver.ResponsibleIndividual` |
| `Vehicles.cs` (file) | `Vehicle.cs` |
| `BoxId(int value)` | `BoxId(int Value)` — matches `ConvoyId` |

Also delete the dead `ConvoyRole` enum in `Driver.cs` — nothing in the solution references it, and
`DriverTeam` models the pairing structurally instead.

**Identity — per entity, not standardised** (the user's explicit instruction). Each entity keeps a
strongly-typed id record whose underlying type matches how that entity is actually referenced:

| Entity | Id | Rationale |
| --- | --- | --- |
| `Vehicle` | none — `VIN` natural key | unchanged; already the route key |
| `Convoy` | `ConvoyId(int Value)` | exists; `dbo.Vehicle.ConvoyId` is already `int` |
| `Box` | `BoxId(int Value)` | exists |
| `Manifest` | `ManifestId(string Value)` | exists; already the reference on the customs queue message |
| `Item` | `Guid` | exists |
| `Person` / `Driver` | **new** `PersonId(Guid Value)` | volunteer PII — not enumerable in a URL |
| `Receiver` | **new** `ReceiverRef(Guid Value)` | matches the existing `dbo.Receiver.ReceiverRef uniqueidentifier` |
| `DriverTeam` | **new** `DriverTeamId(int Value)` | only ever reached through a manifest |
| `Address`, `Route` | none | value objects, stored inline / as ordered child rows |

**Nullability.** Make every navigation property nullable (`Vehicle.Convoy`, `Vehicle.Purchaser`,
`Box.Location`, `Box.Receiver`, `Manifest.Vehicle`, `Manifest.DriverUK`, `Manifest.DriverBorder`,
`Convoy.Route`, `DriverTeam.SecondaryDriver`) and mark genuinely required scalars `required`. This
clears the ~27 `CS8618` warnings named in CLAUDE.md.

**`ManifestStatus` — replace, do not patch.** The current type is a `record` with a `protected`
constructor and six *instance* properties returning `new` instances; there is no way to obtain the
first one and every access allocates. Replace with:

```csharp
public enum ManifestStatus
{
    Created = 0, Proposed, Rejected, Confirmed, Preparing,
    Ready, InTransit, Delivered, Lost, Returned
}
```

and a `ManifestTransitions.CanTransition(ManifestStatus from, ManifestStatus to)` guard encoding
exactly the edges in `docs/manifest-status.puml` — including the single backward edge
`Rejected → Proposed`, and no `Confirmed → Proposed`, no `Delivered → Lost`. Add
`ManifestStatus Status` to `Manifest`.

This guard is the first TDD unit of the whole plan: a table-driven test asserting every legal edge
and a representative set of illegal ones, written before the guard.

**`Manifest.TotalWeightKg()`** keeps the fixed `100 * 2 + 45` padding untouched — key-concepts.md
calls it deliberate.

Files to touch beyond Domain: `Application/Vehicles/VehicleReadModel.cs` (doc comment),
`Api/Vehicles/*`, and the Unit/Component/Integration/BDD projects wherever the renamed members appear.

---

## Increment 2 onwards — one slice per increment

Each slice replicates the Vehicle pattern exactly. **The unit of replication, per entity `Foo`:**

1. `src/…Application/Foos/` — `FooReadModel.cs`, `IFooRepository.cs`, and one file per use case
   (`CreateFoo.cs`, `UpdateFoo.cs`, `DeleteFoo.cs`, `GetFooById.cs`, `ListFoos.cs`), each holding
   *command/query record + outcome enum + handler*. Queries have no outcome enum — absence is `null`.
   Paging is clamped in the list handler (1..200, default 50), not the endpoint.
2. Five `AddScoped` lines in `AddFreedomApplication()` (`Application/DependencyInjection.cs`) —
   explicit, no assembly scanning.
3. `src/…Data/Foos/FooRepository.cs` — Dapper, `private const string Columns`, `CommandDefinition`
   on every call so cancellation is honoured, writes return `affected > 0`, `UpdatedAt =
   SYSUTCDATETIME()` in the UPDATE. One `AddScoped` in `AddFreedomData()`.
4. `iac/local/sql/001-schemas.sql` — append before the final `PRINT`, in house style:
   banner comment, `IF OBJECT_ID(...) IS NULL BEGIN CREATE TABLE ... END GO`, named
   `PK_`/`FK_`/`DF_` constraints, `CreatedAt`/`UpdatedAt datetime2(0)`, **`int` not
   `tinyint`/`smallint` for enums** (Dapper constructor mapping is strict). No new `GRANT` — the
   schema-level grants already cover `dbo`.
5. `src/…Api/Foos/` — `FooRequests.cs` (own request records + `ToCommand()`; the update request
   omits the key so `PUT` structurally cannot change it), `FooRequestValidators.cs` (max lengths
   mirroring column widths; auto-discovered, no registration), `FooEndpoints.cs` with
   `MapFreedomFoos()`, `.AddEndpointFilter<ValidationFilter<T>>()` **then**
   `.RequireAuthorization(...)`.
6. Two policy consts + two `.AddPolicy(...)` in `AddFreedomAuthorization()`; one
   `app.MapFreedomFoos();` in `Program.cs`.
7. Tests, all four levels — see *Testing* below.

Status-code contract, identical to `/vehicles`: list `200`; get `200`/`404`; create
`201` + `Location`, null body / `409` via `Results.Problem`; update `204`/`404`; delete `204`/`404`.

### 2. People — `/people`

`dbo.Person` (`PersonId uniqueidentifier`, names, `DateOfBirth`, `Joined`, `Phone`, `IsDriver bit`,
`Committed bit`). `Driver` is `Person` with `IsDriver` — one table, one slice, no separate
`/drivers` resource. Policies `people:read` (all operational roles), `people:write`
(Administrator only — key-concepts.md makes volunteer access an Administrator responsibility).
Personal data: never logged, per §4.8.

### 3. Convoys — `/convoys`

`dbo.Convoy` (`ConvoyId int IDENTITY`, `Start`, `ExpectedEnd`, `TruckListPublishedAt datetime2(0)
NULL`) and `dbo.ConvoyRouteStop` (`ConvoyId`, `Sequence int`, address columns, PK on the pair) —
`Route : List<Address>` is ordered, so sequence is a real column.

Sub-resources and transitions:
- `PUT /convoys/{id}/route` — replace the ordered stop list.
- `PUT /convoys/{id}/vehicles/{vin}` / `DELETE …` — assign and unassign, writing `dbo.Vehicle.ConvoyId`.
- `POST /convoys/{id}/publish-truck-list` — sets `TruckListPublishedAt`. `docs/process.puml` puts
  *Truck List Published* before *Manifest Proposed*, so this is the precondition the manifest slice
  enforces.

Policies `convoys:read` (all operational), `convoys:write` (Administrator, Dispatcher).

### 4. Receivers — `/receivers`  ← the security-critical slice

Replaces the demonstrative tables the SQL file marks *"Replace these when the Data project grows
migrations"*, keeping their shape and their `DENY`.

- `dbo.Receiver` — `ReceiverRef`, `Organisation`, `Region`, timestamps. Readable by the app
  identity. This is what a manifest and a border-guard view may show.
- `sensitive.ReceiverDetail` — contact name, phone, address lines, city, postcode, `DeleteAfter`.
- `sensitive.ReceiverDetailAccessLog` — one row per read.

**Two connections.** `SqlConnectionFactory` gains a named lookup so `ConnectionStrings:Freedom`
(app identity, `DENY`d on `sensitive`) and a new `ConnectionStrings:FreedomSensitive` (a principal
in the `ground_officer` DB role) are separate. `ReceiverRepository` uses the first;
`ReceiverDetailRepository` uses the second and **writes an access-log row inside the same
transaction as every read** — the audit is not optional and not a `finally`.

Endpoints: `/receivers` CRUD under `receivers:read` / `receivers:write`;
`GET /receivers/{ref}/detail` under a third policy `receivers:detail` restricted to
**GroundOfficer alone**. `AuthenticationExtensions` gains its first `GroundOfficer` role constant.

Non-negotiable tests: the app-identity repository cannot select from `sensitive.*` (integration
test asserting the SQL error, proving the `DENY` is live); a non-GroundOfficer gets `403` on
`/detail`; a successful read leaves exactly one access-log row.

### 5. Boxes — `/boxes`

`dbo.Box` (`BoxId int IDENTITY`, `WeightKg`, `ValidatedByPersonId` NULL, `ValidatedAt` NULL,
location address columns, `ReceiverRef` FK) and `dbo.BoxItem` (`Id uniqueidentifier`, `BoxId`,
`Description`, `PropertiesJson nvarchar(max)` for the open-ended `Dictionary<string,string>`).

- `/boxes/{id}/items` — add, list, remove.
- `POST /boxes/{id}/validate` — the Loader transition. Sets `ValidatedBy`/`ValidatedAt`; `Validated`
  stays computed. key-concepts.md calls this the donor↔UA trust boundary and an **audit artefact**,
  so it is a transition endpoint, never a `PUT` of the two columns, and re-validating an already
  validated box is a `409`.

Policies `boxes:read` (all operational), `boxes:write` (Administrator, Dispatcher, Loader),
`boxes:validate` (Administrator, Loader).

### 6. Manifests — `/manifests`  ← the lifecycle slice

`dbo.Manifest` (`ManifestId varchar(32)` PK, `Vin`, `ConvoyId`, `Status int NOT NULL`,
`DeliveryNotes`, `FerryBookingComplete bit`, `GmrSubmittedAt datetime2(0) NULL`, timestamps),
`dbo.ManifestDriverTeam` (`DriverTeamId`, `ManifestId`, `Leg int` — 0 UK, 1 Border, primary/secondary
`PersonId`s), `dbo.ManifestBox` (`ManifestId`, `BoxId`).

CRUD plus:
- `PUT /manifests/{id}/teams/{leg}` — assign a driver team to the UK or Border leg.
- `PUT|DELETE /manifests/{id}/boxes/{boxId}` — cargo.
- `GET /manifests/{id}/weight` — surfaces `TotalWeightKg()`, padding intact.
- One transition endpoint per edge: `POST /manifests/{id}/propose`, `/approve`, `/reject`,
  `/prepare`, `/ready`, `/depart`, `/deliver`, `/lose`, `/return`.

Three invariants, each a unit test written first:
1. **Every transition goes through `ManifestTransitions.CanTransition`**; an illegal edge is `409`,
   an unknown manifest `404`.
2. **A manifest cannot be proposed against a convoy whose truck list is unpublished** (`process.puml`) — `409`.
3. **Once a GMR has been submitted the manifest is frozen** — recommendations §5.2 answer 2:
   *"Once a GMR is created, no edits can be created to modify the manifest."* Every write path
   (`PUT`, teams, boxes, and every transition except `/deliver`, `/lose`, `/return`) returns `409`
   when `GmrSubmittedAt` is set.

`POST /manifests/{id}/approve` is where the fan-out lives: it enqueues the GMR submission on the
existing `customs-work` queue **and** a manifest-document job **and** driver notifications —
matching the fork in `process.puml`. Policies: `manifests:read` (all operational),
`manifests:write` (Administrator, Dispatcher), `manifests:approve` (Administrator only).

---

## Increment 7 — Manifest document worker

New project `src/UA.Action.Freedom.ManifestWorker` (added to `UA.Action.Freedom.slnx`), copying
`CustomsWorker` exactly: `Microsoft.NET.Sdk.Worker`, environment-variable-only configuration, fail
fast at startup with a message saying *why* the setting matters, everything a singleton.

- `ManifestWorkerService : BackgroundService` — a dumb loop: `Task.WhenAll` of one private loop per
  trigger, `PeriodicTimer` + `SafeWait`, drain-while-work inner loop, catch-cancellation then
  catch-all so it never dies. Following the existing precedent it gets **no test**; all decisions
  live in the processor classes.
- `ManifestDocumentProcessor.ProcessNextAsync(ct) → Task<bool>` — reads `manifest-documents`,
  renders the document, writes to the `manifests/` blob prefix, then the same three-way disposition
  as `GmrSubmissionProcessor`: complete on success, dead-letter on unreadable input or a permanent
  failure, **leave the message alone** on a transient one.
- Ports `IManifestWorkQueue` and `IManifestDocumentStore`, mirroring `ICustomsWorkQueue` /
  `IGmrDocumentStore`, with the adapters left untested by design.

**The redaction rule is the point of this worker.** The rendered document shows vehicle, weights,
box counts, contents categories and a **region-level** destination only — never a street address,
contact name or phone (§4.4.2, key-concepts § Data Sensitivity). Test it the way the existing
privacy guard is written, asserting on the *output* rather than trusting the mapping:

```csharp
document.Should().NotContain("Olena Kovalenko").And.NotContain("Vulytsia");
document.Should().Contain("Kharkiv oblast");
```

Because the API identity is `DENY`d on `sensitive.*`, the worker physically cannot read what it
must not print — the test pins the intent, the database enforces it.

## Increment 8 — Notification worker

Second processor in the same project (`NotificationProcessor`, queue `notifications`), sending SMTP
to Mailpit locally and ACS in Azure. Triggers: driver assigned to a manifest, manifest approved.

The rule with teeth, from §4.3 and key-concepts § Notification: **a notification never contains a
document URL** — only a link to an authenticated page. A test asserts the rendered body contains no
blob endpoint and no SAS token.

## Increment 9 — Infrastructure and docs

- `iac/tofu/storage.tf` — add `manifest-documents`, `manifest-documents-poison`, `notifications`,
  `notifications-poison` to `local.queues` (one resource creating all in sequence; do **not** switch
  to `for_each`, per the comment in that file).
- `iac/local/docker-compose.yml` — a `manifestworker` service alongside the app; Mailpit is already
  there for ACS.
- `iac/local/sql/001-schemas.sql` — a `freedom_sensitive` login/user in the `ground_officer` role
  for the second connection string.
- `.github/workflows/build-and-test.yml` — the `acceptance` job currently starts only
  `app edge website`; add the workers so the new BDD features exercise them.
- `docs/manifest-status.puml` is now authoritative and matches the code; note that in
  `docs/recommendations.md` §5.3 and in `CLAUDE.md` (which currently records the 6-vs-10
  inconsistency and the `Reciever` spelling as open).
- `docs/domain/key-concepts.md` — Truck List publication is now a real transition.

---

## Testing

TDD throughout: no production line without a failing test first. Per slice, all four levels,
matching the existing conventions exactly — sentence-case method names
(`Reports_a_conflict_and_does_not_write_when_the_VIN_is_taken`), `<summary>` on each class saying
*why the rule matters in the domain*, AwesomeAssertions, no `using Xunit;` (implicit `<Using>`).

- **Unit** (`tests/…Tests.Unit/Foos/`) — `FooTestData` factory with every parameter defaulted
  (`AFoo()`, `ACreateCommand()`); a fresh `Substitute.For<IFooRepository>()` inside each test, no
  shared fixture; one test per outcome-enum branch plus one per guard; both query handlers share one
  `FooQueryHandlerTests.cs`. `ManifestTransitions` gets a table-driven test over every edge.
- **Component** (`tests/…Tests.Component/`) — `InMemoryFooRepository` (dictionary-backed, plus
  `Count`/`Contains` for assertions, `params` seeding) and a `FreedomApi.WithFoos(...)` sibling;
  `TestAuthHandler` is reused unchanged. Assert on `JsonElement`, never on the read model, so the
  JSON contract is what is pinned. Every slice needs the authz-negative tests
  (`A_loader_may_not_create_a_convoy`, `A_ground_officer_is_refused_…`).
- **Integration** (`tests/…Tests.Integration/Foos/`) — `[Trait("Category","Integration")]`,
  `ConnectOrSkipAsync` probing the *table* (not just the server) and calling `Assert.Skip`, a
  generated unique key per test, `try/finally RemoveAsync`. The round-trip test asserts full record
  equality — that is what catches a column/CLR type mismatch.
- **BDD** (`tests/…Tests.BDD/`) — one `.feature` per slice, `Background` doing the
  `Assert.SkipUnless` reachability probe plus a destructive pre-clean. **`ScenarioState.CreatedVins`
  and `CleanupHooks` are vehicle-specific and must be generalised first** (e.g.
  `HashSet<(string Resource, string Key)>`) before a second feature is added.
  `FreedomApiClient.ProbeAsync` needs a per-feature route check so an older image skips rather than
  fails. The generated `.feature.cs` is committed — regenerate and commit it.

## Verification

```powershell
dotnet build UA.Action.Freedom.slnx                 # zero CS8618 after Increment 1
dotnet test  --solution UA.Action.Freedom.slnx      # Integration + BDD self-skip without infra

cd iac/local ; cp .env.example .env
docker compose build app manifestworker
docker compose up -d --wait mssql keycloak azurite telemetry
cd ../tofu ; tofu init ; tofu apply                 # picks up the new SQL via filesha256
cd ../local ; docker compose up -d --wait app edge manifestworker

curl http://localhost:8080/health/ready             # all Healthy
dotnet test --project tests/UA.Action.Freedom.Tests.Integration/UA.Action.Freedom.Tests.Integration.csproj
dotnet test --project tests/UA.Action.Freedom.Tests.BDD/UA.Action.Freedom.Tests.BDD.csproj
```

MTP mode: `--solution` / `--project`, and `--filter-class`/`--filter-query` — plain `--filter` matches
nothing.

End-to-end walk, once Increment 6 lands: create a convoy → publish its truck list → assign a vehicle →
create boxes, add items, validate as `loader` → create a manifest, assign both driver teams and the
boxes → `propose` as `operator` → `approve` as `admin` → confirm a `customs-work` message appeared,
a document landed under `manifests/`, and a notification reached Mailpit → confirm any further edit
now returns `409`.

## Sequencing

One PR per increment, each leaving the solution green:
**1** domain + renames → **2** people → **3** convoys → **4** receivers → **5** boxes →
**6** manifests → **7** document worker → **8** notifications → **9** iac/docs.

Increments 2–5 are independent of each other and can be reordered; **6 depends on 3, 4 and 5**, and
**7 and 8 depend on 6**. Increment 1 blocks everything.

Per CLAUDE.md this plan should be copied into `plans/` on the working branch when execution starts,
and progress tracked there.

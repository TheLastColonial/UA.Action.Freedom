# Gotchas and Open Questions

Things that will cost you time, rules that look like bugs but are not, and decisions that are
still outstanding. Written up while building out the API and workers across the domain, so most
entries are here because they actually bit somebody rather than because they seemed likely to.

**How to read this.** §1–§7 are things that are true now and will surprise you (§5a is the French
customs ELO integration; §7 is the operator UI in `web/`; the unnumbered *Observability* section
before it covers telemetry). §8 is work that has been decided but not built. §9 is genuinely
undecided and needs a person. §10 is a per-slice index if you are looking for the history of
one area.

Related: [`recommendations.md`](recommendations.md) for the hosting design and its reasoning,
[`domain/key-concepts.md`](domain/key-concepts.md) for the vocabulary, and `CLAUDE.md` at the
repository root for the working rules.

---

## 1. Tooling and environment

### Parallel agents: several traps live in the generated files

Full guide: [`parallel-agents.md`](parallel-agents.md). The ones that cost time:

- A worktree has **no `.env`** (it is gitignored) and a `.git` *file*, not a directory.
  `agent.ps1 new` generates `iac/local/.env` from the main checkout's; a hand-made worktree has none.
- Compose reads `iac/local/.env` from the directory you run it in. Running bare `docker compose` in the main
  checkout while thinking you are in your worktree starts (or tears down) the **wrong stack**.
- Integration/BDD/Playwright tests default to the main stack's ports (`localhost:1433/8080/8081`);
  without `. ./.agent/env.ps1` an agent silently tests another agent's database.
- A PowerShell function parameter `$Path` shadows a caller's `$path` (variables are case-insensitive and
  dynamically scoped); `agent.ps1` once recorded the registry file as the worktree path because of it.
- Three full stacks on a 16 GB Docker VM run it out of memory; the symptom is `IOException: Cannot allocate memory` from `dotnet watch`'s file poll and restart loops, not a watcher bug (see `parallel-agents.md` Troubleshooting).
- The Azure SDK drops the container from a blob URL when the connection string names a *hostname* (`localhost`, `host.docker.internal`) on a non-default port, so `/ens/x.json` is requested as `/x.json` and Azurite answers `ContainerNotFound`. Always give Azurite an IP (`127.0.0.1`) in connection strings for non-default ports; the generated `.agent/env.*` and `terraform.tfvars` do.
- `dotnet watch` on a bind mount needs `DOTNET_USE_POLLING_FILE_WATCHER=1`, and `node_modules` and `bin/obj`
  must live on named volumes, or Linux output pollutes the Windows checkout.

### `dotnet test` uses Microsoft.Testing.Platform verbs

`global.json` opts into MTP, because the .NET 10 SDK dropped the VSTest bridge `xunit.v3` relied
on. The CLI is different: `--solution <file>` and `--project <file>` rather than positional
paths, and filters are `--filter-class` / `--filter-method` / `--filter-query`.

**The trap:** the old `--filter "FullyQualifiedName~Foo"` syntax is *accepted* and silently
matches nothing, so it looks like your tests passed.

### The integration test assembly must not run in parallel

`tests/UA.Action.Freedom.Tests.Integration/AssemblyInfo.cs` carries
`[assembly: Parallelization(Mode = ParallelMode.None)]`. **Do not remove it.**

These tests share one database. They were parallel-safe while each slice owned an isolated table,
but the foreign keys between `Vehicle`, `Convoy` and `Manifest` changed that — a manifest insert
locks `dbo.Vehicle`, a convoy delete touches it through `ON DELETE SET NULL`, and a paged
`SELECT` walks an index another test is modifying. Different classes then took the same locks in
different orders and SQL Server picked a deadlock victim. It presented as roughly **one failure
in four full-solution runs**, in whichever test happened to lose, which is exactly the kind of
flake people learn to re-run instead of fix.

Also note the xUnit v3 API here: `CollectionBehavior(DisableTestParallelization = true)` is
obsolete **as an error**, and the replacement is `Xunit.v3.ParallelizationAttribute` with
`Xunit.Sdk.ParallelMode.None` — two different namespaces.

### The schema is published by SqlPackage, so `QUOTED_IDENTIFIER` is no longer a trap

The schema used to be applied by `sqlcmd`, which runs with `QUOTED_IDENTIFIER` off, so filtered
indexes (`Msg 1934`) were avoided throughout. The dacpac is now published by SqlPackage, which
sets the ANSI options correctly, so a filtered index is legal again. The existing "one active
row" rules (`BoxQrCode`, `BoxBayAssignment`) are still enforced in their repository transactions;
adding a filtered unique index as a second line of defence is now possible, not yet done.

### sqlcmd resolves `$(NAME)` from the environment

Used deliberately: `database.tf` forwards the two login passwords to the container with
`docker exec -e`, and `iac/local/sql/principals.sql` references them as `$(FREEDOM_APP_PASSWORD)`
and `$(FREEDOM_SENSITIVE_PASSWORD)`. They never appear on a command line, in `docker inspect`, or
in the process table — the same reasoning as the `sa` password. See §3 of `iac/README.md`.
`database/deploy.sh` follows the same rule for SqlPackage by writing the connection string into a
temporary publish profile instead of passing it as an argument.

### Git Bash mangles container paths and SqlPackage arguments

`docker exec freedom-mssql /opt/mssql-tools18/bin/sqlcmd` from Git Bash becomes
`C:/Program Files/Git/opt/mssql-tools18/...`, and `sqlpackage /Action:Publish /p:...` has its
slash-arguments rewritten into paths ("Unrecognized command line argument"). Prefix with
`MSYS_NO_PATHCONV=1`, use SqlPackage's dash form (`-a:Publish -p:...`), or run it from PowerShell.

### Rebuild the image before running the BDD suite

The BDD project talks HTTP to the **deployed containers**, not to code in your working tree.
After a change:

```
cd iac/local
docker compose build app manifest-worker
docker compose up -d --wait app edge manifest-worker
```

Skip this and the suite tests the previous build. It will usually tell you — the reachability
probe distinguishes "the stack is down" from "the running image predates this feature" — but only
for a route that did not exist before.

---

## 2. Database

### Dapper's constructor mapping is strict

Read models are hydrated by constructor, so a column's CLR type must match the parameter exactly.
Use `int` for enums and years, never `tinyint` or `smallint`. This is why `dbo.Vehicle.Transmission`
is an `int` for a three-member enum.

The one place this does not hold is `dbo.BoxItem.PropertiesJson`: item properties are an
open-ended bag, so `BoxRepository` has a private `BoxItemRow` seam that Dapper fills and a mapper
that turns it into the shape the application uses (the category, quantity, value, expiry and code are
real columns on the same row now, and the seam maps them too). That is the exception, and it is commented as
such. A non-string JSON value in the bag is kept as its raw text rather than refused.

### A `date` column does not hydrate a `DateOnly` constructor parameter

Dapper maps a SQL `date` to `DateTime`, so a read model with a `DateOnly` parameter fails with "A parameterless default
constructor or one matching signature ... is required". `DonationRepository` reads into a private `DonationRow`
(`DateTime`) and converts, the same seam `BoxRepository` uses for `ExpiresOn`. Writes are fine: a `DateOnly` parameter
goes in as a `date`.

### The schema is a SQL project, declared as its end state

`database/UA.Action.Freedom.Database` (SDK-style `Microsoft.Build.Sql`) holds one plain `CREATE`
per object — no `IF OBJECT_ID` guards, no `ALTER ... ADD`, no data moves. `dotnet build` turns it
into a dacpac and validates every reference, so an FK to a table that does not exist fails the
build, not the deployment. The old ordering trap (a statement referencing an object created
further down the script) cannot happen: the model has no order.

SqlPackage diffs the dacpac against the target and generates the change: a fresh stack gets a
create, an up-to-date one gets nothing. **Changing the schema means editing the table file**, then
`cd iac/local && docker compose build db-deploy && docker compose up -d --wait db-deploy`.

Four rules that keep it that way:

1. **No migration code in the project.** If an environment with data to keep ever needs a data
   move, it goes in a reviewed Pre/PostDeployment script that is deleted once it has run
   everywhere. Today every stack is rebuilt from scratch, so none exists.
2. **Principals are not in the dacpac.** Roles and `GRANT`/`DENY` are; logins, users and role
   membership belong to the environment (`iac/local/sql/principals.sql` locally, the deployment
   pipeline in Azure). The publish passes `ExcludeObjectTypes=Users;Logins;RoleMembership` so it
   never touches them — remove that and a publish could strip `freedom_app_user` out of its role.
3. **Write CHECK constraints the way SQL Server stores them.** `BETWEEN 0 AND 3` is stored as
   `>= 0 AND <= 3` and `IN (0, 1)` as `= 1 OR = 0`; SqlPackage sees the text differ and drops and
   recreates the constraint on every publish. The `Database` workflow fails if a re-publish is not
   a no-op, which is how you will find out.
4. **Prefer schema-level grants.** `GRANT ... ON SCHEMA::dbo` covers every table added later, so a
   new table needs no grant of its own.

### Test schema changes against a *fresh* database, not yours

Your local volume has data and a history; CI's does not. Reproduce CI before pushing a schema
change — it costs a few minutes:

```bash
cd iac/local && docker compose down -v          # the -v is the point: drop the volumes
docker compose up -d --wait
cd ../tofu && rm -f terraform.tfstate* && tofu apply -auto-approve
```

Dropping the tofu state matters as much as the volumes — otherwise tofu believes the resources it
created last time still exist and skips them, which is a *second* way to not test what CI tests.

### Foreign key delete behaviour is deliberate, per relationship

| Relationship | Behaviour | Why |
| --- | --- | --- |
| `ConvoyVehicle → Vehicle` | `ON DELETE CASCADE` | Deleting a donated vehicle takes its truck-list rows, and their crew and insurance, with it. |
| `ConvoyVehicle → Convoy` | no action | Only **one** cascade path may reach a child, and the Vehicle one above is it. `ConvoyRepository.DeleteAsync` clears the truck list itself, in the same transaction — **inside the `catch`**, because a manifest refuses that statement, not the convoy delete. |
| `ConvoyVehicleCrew` / `ConvoyVehicleInsurance → ConvoyVehicle` | `ON DELETE CASCADE` | Neither has a life without the truck-list entry. Taking a vehicle off an unpublished list is therefore one statement. |
| `ConvoyRouteStop → Convoy` | `ON DELETE CASCADE` | A route has no life without its convoy. |
| `BoxItem → Box` | `ON DELETE CASCADE` | Items have no life outside their box. |
| `BoxQrCode → Box` | `ON DELETE CASCADE` | A label has no life outside its box; a stray one must not outlive it. |
| `ConvoyVehicleBoxAllocation → ConvoyVehicle` | `ON DELETE CASCADE` | Removing an unpublished truck-list entry takes its cargo allocations, not the boxes. The Box and the Vehicle are different roots, so the two cascades are not a multiple-path clash. |
| `ConvoyVehicleBoxAllocation → Box` | `ON DELETE CASCADE` | Deleting a box takes it off its vehicle (plan 16 adds the guard for attested boxes). |
| `ConvoyVehicleFerryBooking → ConvoyVehicle` | `ON DELETE CASCADE` | A booking has no life without the vehicle's entry; withdrawal keeps it. |
| `Manifest → ConvoyVehicle` | no action | Composite, on `(ConvoyId, Vin)`. A manifest is the record of what a vehicle carried across a border, so neither cancelling a convoy nor deleting a vehicle may erase it — and a manifest cannot name a truck that is not on the convoy. |
| `Box → Person` (validator) | no action | A volunteer who leaves must not take the record of what they signed for. |
| `ReceiverDetail → Receiver` | no action | Makes "delete the reference, keep the address" impossible. |

### A telemetry test flakes only in a full-solution run

`GmrSubmissionProcessorTelemetryTests.The_submission_is_traced_as_a_consumer_span_linked_to_the_
approval_that_queued_it` fails about one `dotnet test --solution` run in four, and passes every
time the Unit project is run on its own. It is a **cross-assembly** race: the Unit and Component
assemblies run in parallel, both start `ActivityListener`s over `UA.Action.Freedom.*` sources, and
the span this test asserts on is occasionally sampled by the other listener first.

Not caused by, and not fixed by, anything in the convoy/manifest consolidation — the Customs Worker
is untouched by it. Noted here so the next person does not go looking for a real failure. The fix,
when somebody wants it, is for the telemetry tests to scope their listener to a per-test
`ActivitySource` name rather than the shared prefix.

### The truck list is a table, and everything about a journey hangs off it

`dbo.ConvoyVehicle`, keyed `(ConvoyId, Vin)`, is the single statement of "this vehicle is travelling
with this convoy". Before it, that fact was written in four places and reconciled in none:

- `dbo.Vehicle.ConvoyId`, a mutable pointer;
- `dbo.Manifest.ConvoyId` + `Vin`, two independent nullable foreign keys;
- the leading columns of the crew table;
- and the leading columns of the insurance table.

Two things went wrong with that, and both are worth knowing because they look like features:

1. **An arrived convoy lost its own truck list.** Arrival nulled `Vehicle.ConvoyId` to *release* a
   Returned vehicle, so "which vehicles were on convoy 5?" became unanswerable — while the crew and
   insurance rows went on naming a parent that no longer existed.
2. **Nothing checked a manifest's vehicle was on its convoy**, and nothing stopped one vehicle
   carrying two manifests. That mattered because `ArriveAsync` asks each vehicle for its finished
   manifest: two would have been satisfied by whichever finished first.

Both are now structural. `Vehicle` has no `ConvoyId` at all — `VehicleReadModel.ConvoyId` is derived
from the truck list on the way out — and `Manifest.(ConvoyId, Vin)` is `NOT NULL`, a composite
foreign key and unique. **Do not add a convoy column back to `dbo.Vehicle`**, and do not relax that
uniqueness.

### The route is merged, not replaced, and the id is the point

`PUT /convoys/{id}/route` used to delete every stop and insert the new ones. A route point now has a stable
`RoutePointId` (the `IDENTITY` primary key of `dbo.ConvoyRouteStop`, with `UQ (ConvoyId, Sequence)`), so
`ConvoyRepository.ReplaceRouteAsync` is a merge in one transaction: delete the points not sent, **park the survivors on
negative sequences** (`Sequence = -RoutePointId`, because the unique key is checked row by row and a reorder would otherwise
collide with itself), update the matched points, insert the new ones. Anything that will refer to a point must be
reported through `IRoutePointReferences` (Application), which the handler asks before removing points (`409
route-point-in-use`): the default answers "nothing", plan 11 and plan 18 replace it, and each should also add a real foreign key to
`RoutePointId`. A test double must keep the same rule (`InMemoryConvoyRepository.ReferenceRoutePoint`). A Border point has an
authority and no other kind does; that is validated in the API, not by a CHECK that spans two columns, because SQL Server
normalises such a check and every publish would recreate it.

### The Convoy Leader is a nomination with history, and the crew cannot silently lose them

`dbo.ConvoyLeaderAssignment` holds one open row per convoy (`UX_ConvoyLeaderAssignment_OpenPerConvoy`, filtered on
`Until IS NULL`). `NominateAsync` checks, under lock and in the same transaction, that the person is a **Driver** crewed on
the convoy, closes the open row and opens the new one. Removing the current leader from the crew is refused
(`409 convoy-leader-on-crew`) until another leader is nominated: the check is in `UnassignCrewFromVehicleHandler`, not in SQL,
so a nomination and a removal racing each other could still slip through; plan 13 makes "leader assigned" a blocking readiness
requirement, which catches it before departure.

### There is one crew record, and the manifest reads it

`dbo.ConvoyVehicleCrew` (was `dbo.VehicleDriver`) carries a `Role` (there are no journey legs), and
`dbo.ManifestDriverTeam` is gone. They used to coexist, unconnected by any foreign key or join:

- the crew table decided the **insurance**, which names the drivers it covers, and therefore whether a
  manifest could depart;
- the manifest's own primary/secondary teams decided **nothing at all**.

So a printed manifest could name a crew the insurance had never heard of, and
`SetManifestTeamHandler` checked only that the person was a registered driver — never that they were
in the vehicle. Crewing is now one act, on the truck-list entry, and `GET /manifests/{id}/crew` is a
read of it. **Do not give the manifest a crew of its own.**

One seat per person is **per convoy** (`UQ_ConvoyVehicleCrew_Convoy_Person`). The journey leg, which let a
person change vehicle at the European border, is removed ([ADR 0007](adr/0007-journey-legs-are-removed-from-the-crew-model.md)).

**Insurance is not voided by a crew change.** `dbo.ConvoyVehicleInsuranceDriver` holds the drivers a policy
names. Removing a driver deletes their row in the same transaction and the policy stays in cover. A driver added
afterwards has no row, which is what `VehicleInsuranceReadModel.UncoveredDrivers` reports, until the Dispatcher records
the insurance again. The `InMemoryConvoyRepository` fake mirrors this, and `WithCrew` seeds a driver as already
covered when the vehicle is insured.

### The GMR carries the plate, not the VIN

`GmrSubmissionRequest.VehicleRegistration` and `ManifestDocumentRequest.VehicleRegistration` are
both documented as "the plate the border expects to see", and both were being handed `Manifest.Vin`
— the chassis number, which is not on the front of the vehicle. `IManifestRepository` grew a
`GetVehiclePlateAsync` for it. If you add another hand-off that names a vehicle, use the plate.

### `dbo.ConvoyVehicleBoxAllocation` is keyed on `BoxId`, not the pair

A box is on **at most one** truck-list entry. The same box on two vehicles would be declared twice
at a border and arrive once. `AllocateBoxAsync` therefore *moves* a box rather than duplicating it, in one
transaction that locks the entry (`UPDLOCK`) and the box's existing allocation (`UPDLOCK, HOLDLOCK`) so two
dispatchers racing for one box take turns. It replaced `dbo.ManifestBox`, which was keyed the same way against the
manifest. A local database built before plan 07 keeps the old table and the `Manifest.FerryBookingComplete` column
(a publish does not drop them), and the `LastChangedGuardTests` then fail on `dbo.ManifestBox`: drop them by hand or
reset the database.

### `BoxRepository.IssueQrCodeAsync` and `AssignBayAsync` also hold a transaction

Re-labelling a box is one act: the old token must stop resolving at the instant the new one
starts. `IssueQrCodeAsync` revokes any active `dbo.BoxQrCode` row and inserts the new one inside
`BeginTransactionAsync` — the same reasoning as `ConvoyRepository.ReplaceRouteAsync` and
`ReceiverDetailRepository.ResolveAsync` (§ per-increment index): each transaction here is scoped
to one aggregate's read-then-write. `BoxRepository.AssignBayAsync` follows the identical shape
for shelving a box — vacating its current bay and writing the new assignment must not be split.
Do not "simplify" any of these into two separate calls: a failure between them leaves a box with
no label a scan resolves to, or in two bays at once.

"At most one active label per box" is enforced by that method — the revoke is
`WHERE BoxId = @boxId AND RevokedAt IS NULL`, so the database settles a concurrent double-issue —
**not** by a filtered unique index, which the old sqlcmd-applied script could not create (see
§ Tooling; SqlPackage now could). `ResolveActiveQrCodeAsync` and `GetActiveQrCodeAsync`
both filter `RevokedAt IS NULL`, so a revoked token reads as unknown rather than resolving to a
box it no longer names.

### QRCoder is the first drawing dependency — use only its managed renderers

`src/UA.Action.Freedom.Api` references `QRCoder` for the box QR image and label. Use
`SvgQRCode` and `PngByteQRCode` (pure managed). The `QRCode` type is `System.Drawing`-based and
pulls a native dependency that is absent from the Linux image the API ships in — it must not be
used, and the same applies to any future use of QRCoder in a worker or the Functions target.
`QrCodeRenderer` / `BoxLabelRenderer` are pure and deterministic so the endpoint tests can pin
their output; the label renderer's signature carries no receiver data, which is what makes the
"no delivery detail on a label that travels" rule structural (§ Security invariants — *Redaction
is structural, not a rule*).

---

## 3. Security invariants that are easy to break

These are the ones where a plausible-looking change quietly removes a control.

### The application does not connect to SQL as `sa` — and must not

**This was found the hard way.** `DENY SELECT ON SCHEMA::sensitive TO freedom_app` is the control
`recommendations.md` §4.4 calls load-bearing. It was completely decorative, because the
application connected as `sa`, and **`sa` is sysadmin, which bypasses permission checks
entirely**. Every test written on top of it would have been theatre.

There are now three logins (see `iac/README.md`): `sa` applies the schema and nothing else,
`freedom_app` is the application's identity and is `DENY`'d on `sensitive`, and
`freedom_sensitive` is in the `ground_officer` role and is the only way to read a delivery
address. `ReceiverSegregationTests` asserts the denial against the real database — and only means
anything because of this.

### Scope is decided per request from assignments, and three things about it are easy to break

Resource-scoped permissions (ADR 0010, plan 17) have no token claim to inspect, so a few things look odd.

- **`ConvoyLeader` is derived by `LeaderRoleClaims`, an `IClaimsTransformation`, and the transformation must not ask `ICurrentPerson`.** It runs inside authentication, before `HttpContext.User` is set, and `ClaimsCurrentPerson` memoises its answer, so asking it there would resolve the caller as unlinked for the whole request. It takes the subject from the principal it is given and asks `IPersonRepository` directly.
- **A forgotten route is the dominant risk.** `ScopeEndpointMapTests` fails the build for any endpoint a Loader or Convoy Leader can reach under a `boxes:*`, `locations:*` or `convoys:read-led` policy that has neither scope metadata nor `ScopeExempt(reason)`, and pins the exempt list. List handlers take a `LocationVisibility` with no default for the same reason. A new box-bearing route needs one of the two.
- **Roles union, so `operator` is not scoped.** A login holding Dispatcher, Purchaser or Administrator reaches everything those roles reach whatever else it holds. To see a Loader's scope locally use the `loader` login. In component tests the default fake gives the test caller every location (`ForTheTestCaller`); a test about scope builds its own assignments.
- Leader scope does not yet end at convoy closing: plan 14 adds `ClosedAt` to the same question (`IConvoyLeaderRepository.IsCurrentLeaderAsync` and `LedConvoyIdsAsync`), and plan 19 must read address access through it.

### Receiver detail is protected three separate ways

Each holds if the others are removed by mistake:

1. **The `receivers:detail` policy** — GroundOfficer alone, not even Administrator. Administering
   access is not the same as holding it.
2. **A separate connection factory *interface*** — `ISensitiveDbConnectionFactory`, not a named
   lookup on the existing one. A repository asking for `IDbConnectionFactory` *cannot* be handed
   the Ground Officer connection, and widening that needs a constructor change a reviewer sees.
3. **The database `DENY`.**

`DELETE /receivers/{ref}` sits behind `receivers:detail` rather than `receivers:write`, because
removing a receiver removes its address. That is *not* `receivers:register`: registration (`PUT /receivers/{ref}/status`) is the Administrator's
act of authorisation, while deletion reaches into the `sensitive` schema and so is the Ground Officer's. The two policies differ on purpose.

Deleting a receiver that a box (`FK_Box_Receiver`) or a vehicle (`FK_ConvoyVehicle_HandoverReceiver`) still names fails on the foreign key *after* its
address has been removed, and answers 500 rather than 409. It predates registration for boxes, and registration adds a second way to reach it; the
receiver's `GET /receivers/{ref}/usage` is how to see what names one first.

### Redaction is structural, not a rule

Three types deliberately have nowhere to put a street, contact or phone, and reflection tests
assert their field lists stay that way:

- `ReceiverReadModel` — what `/receivers` returns.
- `ManifestDocumentLineReadModel` / `ManifestDocumentRequest` — what goes on the document queue.
- `GmrSubmissionRequest` — what goes to HMRC.

Code holding one of these has nothing sensitive to leak, so document generation and logging are
safe without either of them remembering a rule. **The Manifest Worker has no database access at
all** for the same reason: the application composes the document and queues it, so the worker
could not read an address even if someone tried.

### The audit is a parameter of the read

`IReceiverDetailRepository.ResolveAsync(ref, principalId, reason, ct)` — there is no way to spell
"read the address but do not log it". The log row and the `SELECT` commit in **one transaction**,
so a disclosure cannot happen without its entry; attempts are logged even when no address exists;
and the trail outlives the address it describes. The principal comes from the token, never the
request body — a trail the caller could write their own name into would not be one.

### Validation responses must not echo the data they rejected

Validation bodies reach client logs and browser consoles. Messages name the field and never quote
the value. There are tests asserting a 400 contains no phone number (`/people`) and no street
(`/receivers/{ref}/detail`).

### Queue messages are durable and widely readable

Anything holding the storage credential can read them, so they are the wrong place for delivery
detail. Both queue contracts carry a manifest reference and operational facts only.

---

## 4. Domain rules that look like bugs

Do not "fix" these without asking.

### The fixed 200 kg and 45 kg on a manifest weight

Two drivers and their bags, plus a fuel allowance. A deliberate border-check estimate, stated
outright in `domain/key-concepts.md`. `GET /manifests/{id}/weight` returns the **breakdown**
rather than a single total specifically so the allowances are visible instead of looking like an
arithmetic error.

### `unvalidatedBoxCount` on the weight response

An unvalidated box weighs zero until a Loader says otherwise, so a total containing one is
provisional. The count is the honesty flag that stops it reading as a confirmed figure.

### A frozen manifest still moves

`recommendations.md` §5.2 forbids **edits** after the GMR exists, not **progress**. This was got
wrong first time: blocking every transition stranded approved manifests in `Confirmed` for ever —
they could never be prepared, loaded or delivered. `Preparing → Ready → InTransit → Delivered`
report what happened to a load HMRC already knows about and contradict nothing.

What the freeze actually blocks: `PUT`, team assignment, cargo changes, delete, and *reopening*
to `Proposed`/`Rejected`. That last is a guard against a future backward edge rather than a path
anything takes today.

### Publishing a truck list closes it to additions, not to departures

Adding a vehicle after publication is a `409`: a manifest would otherwise be proposed against a set
that is still moving. **That much is an inference, not a quoted requirement** — see §9.1.

Removing one is not refused, because vehicles break down. After publication
`DELETE /convoys/{id}/vehicles/{vin}?reason=` is a **withdrawal**: `ConvoyVehicle.WithdrawnAt` is
stamped and the entry, its crew, its insurance and its manifest all stay. Deleting them would strand
a manifest that still describes a real load, and lose the record of which convoy the vehicle set off
with. A withdrawn vehicle is skipped by readiness and arrival and may join a later convoy.

### A box cannot be changed after validation

No items in or out, no new receiver, no second validation. The Loader's confirmed weight is what
the border check relies on; any of those would leave it describing something no longer true.

### An item names a category, and "expired" is a date, not a category rule

Plan 05 (ADR 0014) gave an item a typed `CategoryId`, `Quantity`, `ValueGbp`/`ValueSource` and `ExpiresOn`. These are easy to trip on:

- **`ExpiresOn` is a `date`, and *expired* needs no category.** An item is expired once the date is before today (UTC), which `ValidateBoxHandler`
  decides from the item alone. *Short-dated* is the part that needs the category's `WarnWithinDays`, and it is worked out at read time by
  `ListBoxItemsHandler`, never stored. An item expiring today is not expired. Do not "optimise" the validate check into the repository query without
  keeping the already-validated answer: a box validated last month whose item has since expired still answers `409 already validated`, not `box-has-expired-items`.
- **The shelf-life thresholds are unverified (D25) and are data.** Medicine 180 days and food 90 are placeholders in `dev-seed.sql`. They are columns on
  `dbo.ItemCategory`, changed by an Administrator through `PUT /categories/{id}`, so nothing about them needs a release. Keep them out of code.
- **`PropertiesJson` holds strings only.** The seed once stored `{"quantity":10}`, a JSON number, which `Dictionary<string,string>` refuses to read and
  turned the whole item list into a `500`. The reader now keeps any non-string value as its raw JSON text, and `quantity` has a column of its own, so the
  seed no longer writes one. A new typed field belongs in a column, not in the bag.
- **`POST /boxes/{id}/items` answers `200` with `{ itemId, warnings }`, not `204`.** A not-carried or already-expired item is *accepted*: the Loader has to see it
  to take it out. A client that treats only `204` as success will think a perfectly good add failed.
- **There is no `DELETE /categories`.** A category cannot be removed once an item names it (`FK_BoxItem_Category` is NO ACTION), and nobody has asked for deletion.
  The BDD suite therefore finds its fixture categories by name instead of creating one per scenario.
- **The fixed list is seed data, not part of the dacpac.** A database that was never seeded has no categories and nothing can be packed until an
  Administrator adds one. See §9.


### Only a Passed vehicle joins a convoy — and it cannot be moved from another

`PUT /convoys/{id}/vehicles/{vin}` is a 409 unless the vehicle's inspection is `Passed`, and a
409 if it is already travelling with a *different* convoy. The second rule closed a hole: assigning
to convoy B used to silently take the vehicle off convoy A, even when A's truck list was already
published and manifested. Both rules are in the conditional `INSERT`'s `WHERE`, so a Mechanic
failing the vehicle at the same moment cannot race it on.

"Travelling with another convoy" is an un-withdrawn `ConvoyVehicle` row on a convoy that has not
arrived. A vehicle whose convoy has arrived without handing it over, or which withdrew from one, is
free — and nothing has to be cleared for that to be true.

### A volunteer still named anywhere cannot be deleted

None of the five foreign keys onto `dbo.Person` cascades — they are the record of who crewed,
validated and shelved what. `PersonRepository.DeleteAsync` catches the FK violation (547) and the
API answers 409, rather than the 500 it used to. See §9 Q9 for the erasure question this raises.

### Who did it comes from the login, and an unlinked login is refused

`ICurrentPerson` (`Api/Configuration/ClaimsCurrentPerson.cs`) is the one answer to "who is calling": the token
subject (`NameIdentifier`, then `sub`) looked up through `IPersonRepository.FindBySubjectAsync`, which reads
`dbo.PersonDetail` only. A missing claim, an unlinked login and an erased volunteer are all `NotLinked`, and the
endpoint returns `403` with problem type `login-not-linked` (`LoginNotLinked.Problem()`) **before** any handler runs, so
nothing is written. There is no "unknown" fallback and the box and bay request bodies no longer accept a person id
(an extra `validatedByPersonId`/`assignedByPersonId` is ignored). Insurance records `RecordedByPersonId` (FK to
`dbo.Person`, so the recorder's row survives their erasure as "Former volunteer"); the receiver access log records
`PersonId` with **no** foreign key, like its receiver, and deliberately not the token subject, which is erasable.
The Keycloak subject of a seed user is generated per realm import, so a recreated realm needs its logins linked again.

### Every write is attributed, so most people are named by some row

ADR 0017 made `LastChangedBy` a foreign key to `dbo.Person` on every entity table. Three consequences worth knowing before touching it.

- **Erasure mostly stamps `ErasedAt`.** Almost any volunteer who has made a change is now named by a row, so `PersonRepository.DeleteAsync`'s
  `DELETE dbo.Person` hits the foreign key and falls back to stamping `ErasedAt`; only a volunteer nobody's row names is removed outright. The
  personal data is deleted either way. The `UPDLOCK` and the foreign-key fallback rely on `XACT_ABORT` being off on the app connection.
- **"Former volunteer" is decided in one place, `dbo.PersonDisplay`.** It LEFT JOINs `PersonDetail`, so an erased person has no name to join. Do not
  rebuild the `COALESCE` in a query; join the view. The in-memory twin is `PersonDisplay.Erased` in the component tests.
- **The attribution is request state, not an argument.** Repositories read `IChangeAttribution`, filled by `ChangeAttributionMiddleware` from the
  login after authorization. A repository used outside a request (a test, a job) gets whatever the holder was given — NULL if nothing — so an
  integration test says who it is with `SqlTestDatabase.AttributedTo(person)` or `Unattributed`. And `LastChangedAt` is stamped even when
  `LastChangedBy` is NULL (a login nobody had linked created the volunteer it is about to link): it records *when*, and never invents a *who*.

The schema guard (`LastChangedGuardTests`) reads `INFORMATION_SCHEMA` as `freedom_sensitive`, not `freedom_app`: the `DENY` on the `sensitive` schema
hides its tables from the catalogue too, so the app's own identity would pass a guard that could not see them.

### A donor is erased, never refused, and is not a volunteer

`DonorRepository.EraseAsync` copies the volunteer pattern (delete the detail, try to delete the identity, stamp
`ErasedAt` on a foreign-key violation) but has no refusal: a donor has no operational dependency, so there is no
`StillActive`. Erasure of a donor never touches a volunteer and the reverse; they are separate tables with separate
keys, and `DonorRepositoryTests` asserts it. The donor report is the other place this matters: it must keep working
after erasure, so it asks `dbo.DonorDisplay` for the name rather than reading the detail row.
[Q-retention](domain/decisions.md#q-retention) (how long a donor's details are kept) is still open, as is the lawful
basis for entering a donor's details from an email: that is the charity's decision, not the code's.

### `Committed` requires `IsDriver`

Commitment is a commitment to *drive on a convoy*. Letting the two disagree would put a non-driver on
the dispatcher's committed-driver shortlist.

---

## 5. The write-once transition pattern

Four records are stamped once and then freeze their aggregate. They are all built the same way,
and a fifth should follow it:

| Record | Endpoint | Freezes |
| --- | --- | --- |
| `Convoy.TruckListPublishedAt` | `POST /convoys/{id}/publish-truck-list` | the convoy's vehicle list, to additions |
| `Box.ValidatedAt` + `ValidatedByPersonId` | `POST /boxes/{id}/validate` | the box's contents, weight and receiver |
| `Manifest.Status` + `GmrSubmittedAt` | `POST /manifests/{id}/approve` | the manifest's content |
| `ConvoyVehicle.WithdrawnAt` + `WithdrawnReason` | `DELETE /convoys/{id}/vehicles/{vin}?reason=` | the vehicle's part in this journey |

The shape, in all four cases:

- **Absent from the request body and from the `UPDATE` statement.** There is no way to set, clear
  or forge them through an ordinary edit. Tests send those fields anyway and assert nothing moves.
- **The transition's SQL is conditional** — `AND TruckListPublishedAt IS NULL`,
  `AND ValidatedAt IS NULL`, `AND Status = @from`, `AND WithdrawnAt IS NULL` — so the *database*
  settles a race between two people pressing the same button, rather than a read-then-write in C#.
- **`ConfirmAndFreezeAsync` goes further** and does both in one statement with `OUTPUT INSERTED`.
  A manifest that is Confirmed but not yet frozen is editable, and that window is the thing §5.2
  rules out.

### Approval signs off and files nothing; filing enqueues, then stamps

Approval confirms and freezes in one statement (`ConfirmAndFreezeAsync`) and does nothing else: no ENS is needed,
and no GMR, document or envelope is enqueued, in either submission mode.

Filing a declaration is a separate act. In manual mode (the default) a Dispatcher records the reference they
obtained in the authority's portal. In automatic mode `POST .../declarations/{gmr|elo}/file` **enqueues first and
stamps the declaration `Filed` second**, so a failed enqueue leaves it unfiled and retryable; `HandOff` counts
`freedom_manifest_approve_partial_failures_total{stage}` (stages `gmr`, `elo`, `document`) and logs the exception
*type* before rethrowing. There is a `Received.InOrder` test on it.

**Automatic `Filed` means *enqueued*, not *accepted*.** The workers have no database, so nothing writes the
authority's answer back onto the declaration. A green declaration in automatic mode proves the message was
queued; the envelope or GMR document in blob storage proves the worker got an answer.

**A reference is write-once, and an ELO needs an accepted ENS.** `RecordReferenceAsync` is one conditional `UPDATE`
(`WHERE Reference IS NULL OR Status = Refused`), so two dispatchers resolve in the database. A refused declaration is
recorded afresh by recording again. Deleting a convoy or a vehicle that has declarations is refused (`409`): the
foreign key to the truck-list entry is NO ACTION on purpose.

**Index the foreign keys to `dbo.Person` on any new table.** Every erasure checks them; an unindexed one scans the
table under lock, and `dbo.Declaration` deadlocked the BDD suite's person erasure against a declaration being filed
until `IX_Declaration_RecordedBy` and `IX_Declaration_LastChangedBy` were added.

---

## 5a. French customs (ELO)

### An ELO is not a declaration, and cannot leak anything

`ENV_CRE01` is crossing flags plus a list of declaration identifiers issued by *other* systems. It
has no field for goods, weights, a consignor, a consignee, an address or even a registration. So
unlike the manifest document — which had to be kept narrow on purpose — the envelope's redaction is
free: `EloEnvelopeRequest` carries a manifest reference and a crossing profile because there is
nothing else to carry. A component test pins the serialised property set so that stays true.

### UK → France is `IMPORT`, not `EXPORT`

The crossing direction is named from the French border's side. Version 1.1.0 of the service contract
renamed these from `ENTREE`/`SORTIE`, and the new names read like an exporter's point of view without
being it. The original WireMock stub said `EXPORT`; it was wrong and is now `IMPORT`.

### TIR/ATA halves the paperwork, and is a ruling rather than a setting

Issue #24 records French Customs' guidance: *"For emergency humanitarian aid destined for Ukraine,
select TIR/ATA without transport contract."* Under **ENV_CTR_RG08** a loaded lorry arriving in France
*outside* TIR/ATA must present an ENS **and** a customs-clearance formality; under TIR/ATA the ENS
alone suffices. That is why `EloCrossingProfile.HumanitarianAidToUkraine` lives in the domain with
its citation and not in configuration — flipping `estTIRATA` doubles what a dispatcher must obtain
before a convoy can sail.

### `functionalId` means different things on different calls

On **create** it may be the `correlationId` — the envelope number does not exist yet. On **modify**
and **retrieve** it *must* be the envelope number (`numeroDossier`). Getting it wrong is accepted by
the local stub and rejected by real customs.

### The barcode field was a blocking codegen bug

The published spec declares `pdf: { type: string, enum: [formatbytebase64] }` — a single-value enum
naming an encoding — while its own worked examples send real base64. NSwag faithfully generated a
one-member C# enum with a `JsonStringEnumConverter`, which **throws on every successful create and
retrieve**. Same defect class as the `HMRC.PushPullNotifications` `messageContentType` bug, except
blocking rather than cosmetic, and `src/EDI.ELO/README.md` used to call it harmless.

Fixed in `build/nswag/elo.preprocess.json` via the new `overrideProperties` knob, as plain
`type: string` rather than `format: byte` — NJsonSchema would emit `byte[]` and decode inside the
deserialiser, turning a malformed value into a crash instead of something a worker can handle.

**The same sidecar is why `-Api elo` no longer requires `-Raw`**: `preserveHeaderParameters` carves
`Authorization` out of the transport-header strip, which was the original reason for that rule.

### The spec contradicts itself about where `pdf` sits

The schema puts it inside `enveloppe`; every worked example puts it beside it. Both placements are
bound, and the processor reads whichever arrived. Do not "tidy" this into one.

### Nothing is dead-lettered after customs has accepted an envelope

Creating an envelope is **not idempotent**, and the `numeroDossier` that identifies it exists only in
the creation response. So once the create succeeds, no later failure may poison the message:

- a barcode that is absent or not base64 → store the reference **without** it and complete;
- a store that fails → leave the message, and accept that the retry makes a second envelope.

A second envelope at customs is unwanted but visible and recoverable. A forgotten `numeroDossier` is
an orphan nobody can name, modify or present at a border. `EloEnvelopeProcessor` carries a `created`
flag for exactly this, and the 4xx catch is guarded by `!created`.

### An envelope naming no declaration is refused locally

ENV_CTR_RG08 again: a loaded lorry must name at least one formality. The processor dead-letters that
before calling customs, because the answer is already known and a refused create still spends a
request. The producer-side adapter refuses it too, with a message naming the missing setting — by
then the manifest is frozen, so the error has to be actionable.

### WireMock loads its mappings at boot

Editing a file under `iac/local/wiremock/mappings/` does nothing until
`docker compose restart wiremock`. This cost a debugging session: the ELO journey passed every step
except the barcode, because the container had been up for a day with the previous stub.

---

## 5b. ICS2 (the Entry Summary Declaration)

### The Shared Trader Interface is not an HTTP API

It runs on EU **eDelivery AS4** — ebMS3/AS4, SOAP 1.2 with attachments — with a TLS certificate, a
separate eIDAS sealing certificate from a LOTL-listed CA registered in UUM&DS, and a mandatory
self-conformance run before production. Every other third-party integration here is an NSwag-generated
client from a committed OpenAPI spec, and **that pattern does not transfer**. There is no
`src/ICS2.*` project and no `build/nswag` entry, and adding one would produce a client that cannot
connect. AS4 also needs a permanently reachable *inbound* endpoint, which `recommendations.md` §4.1
declines on purpose.

So Freedom records the MRN a Ground Officer obtained in the EU Customs Trader Portal.
`IEnsDeclarationStore` is where an IT Service Provider's adapter drops in later — that is a procurement
decision, not a coding one. See `docs/adr/0003`.

### Approval refuses *before* it freezes, and that is the one exception

Everywhere else in the manifest lifecycle the rule is freeze-then-enqueue, because an editable manifest
whose GMR is on its way is what §5.2 forbids. `ApproveManifestHandler` checks for a recorded ENS before
`ConfirmAndFreezeAsync` instead, and the asymmetry is deliberate: a manifest frozen with no declaration
is frozen for ever against an envelope French customs will never issue. There is a test asserting that
nothing was frozen and nothing enqueued.

### A recorded MRN is write-once, enforced by blob storage

`ens/{manifestId}.json` has no `WHERE` clause to be conditional on, so `BlobEnsDeclarationStore.SaveAsync`
uploads with `IfNoneMatch = ETag.All` — create, never replace — and reports the 409 rather than throwing.
That is the blob equivalent of the conditional `UPDATE` behind `Manifest.GmrSubmittedAt`, and it is only
provable against a real storage account: a substituted blob client would show that we *pass* a condition,
not that the service honours it. Hence
`tests/UA.Action.Freedom.Tests.Integration/Manifests/EnsDeclarationStoreTests.cs`, and hence the
Integration project referencing the API project at all.

### Non-amendable fields mean invalidate-and-refile, not correct

Mode of transport, the declarant, the customs office of first entry, the carrier identifier, transport
document references and the goods item number cannot be amended once filed. Neither can a ferry's
**vessel IMO**, which "must not be subsequently modified" once entered. Correcting any of them means
invalidating the declaration in ICS2 and lodging a new one — which is why `DELETE /manifests/{id}/ens`
exists, why it copies before it deletes, and why the goods item number comes from a stable `ORDER BY`
rather than whatever the database felt like returning.

### Mode of transport describes the crossing, not the vehicle

A lorry accompanied on a **ferry** is declared maritime (**1**). A lorry on **LeShuttle** is declared
road (**3**) even though it travels on a train. **Rail (2) is not accepted at the Brexit Smart Border.**
This is why `dbo.Convoy` has `CrossingMode` at all, and it is a non-amendable field, so getting it wrong
costs a refiling rather than an edit.

### The filing sheet is deliberately incomplete

`GET /manifests/{id}/ens/filing-sheet` has no field for a street, a postcode or a contact, and a
component test asserts the serialised body contains none. An ENS genuinely needs the consignee's
address; the filer is a Ground Officer and already holds it, so the sheet names the consignee at
organisation and region, sets `consigneeAddressWithheld` and points at
`GET /receivers/{ref}/detail` — because a filer who saw no address might otherwise conclude there is
none. The address goes from their screen into the portal and never through a Freedom queue, blob or
payload.

### The ENS MRN cannot go on the GMR, and the field that looks right is the wrong one

`customsDeclarations[].customsDeclarationId` is the **CDS** identifier — a DUCR for an outbound
movement. The right field for an ENS is `sAndSMasterRefNum`, *"the Movement Reference Number for a
Safety & Security declaration"*, but it hangs off a declaration container, and every container requires a
primary identifier Freedom does not hold: a CDS DUCR, a TIR carnet number or an ATA carnet number. The
spec also scopes ICS2 MRNs to the `GB_TO_NI` direction while these movements are `UK_OUTBOUND`. So
`GmrSubmissionRequest` carries no MRN, and a property-set test pins that with the reasoning attached.

### The filing type code is unconfirmed, so nothing asserts one

Secondary sources disagree between **F50** and **F40** for a complete road ENS, and one page on the same
site gives both. The authority is the ICS2 Functional Specifications in the CIRCABC group
`18fb5859-3970-4ac5-b30b-6604977a15a7`. The filing sheet carries the mode-of-transport code, which is
unambiguous, and leaves the dataset code to the portal. Do not hard-code one from a blog post.

### The MRN is never a metric tag, span attribute or log field

It is a customs reference tied to one consignment, so it belongs with plates, VINs, EORIs and route
stops on the list in § Observability below — not on a bounded-set tag.

---

### Declaration staleness is derived on read, and the snapshot is versioned (plan 09)

- **`Stale` is never written by a read.** `ListDeclarationsHandler` swaps `Filed`/`Accepted` for `Stale` in the read
  model when `Staleness.IsStale(snapshot, currentLoad)`; the row keeps its stored status until `withdraw`. So code that
  asks the *repository* for a declaration (the ELO's "accepted ENS" check, `RecordReferenceAsync`) sees the stored
  status, not the derived one. Plan 13 must judge currency with `DeclarationStaleness`, not by reading `Status`.
- **A snapshot is compared on the fields its `SnapshotVersion` has.** Adding a field to `LoadSnapshot` means a new
  `Version` and a new branch in `Staleness.Fingerprint`; the older branch stays untouched. Otherwise every filed
  declaration goes stale the day the field ships. Unknown JSON properties are ignored on read for the same reason.
- **The first snapshot stands.** `StoreSnapshotAsync` only writes where `SnapshotJson IS NULL`, and `MarkReadyAsync`
  refreshes it only while the declaration is Draft or ReadyToFile. A refused declaration recorded afresh has its
  snapshot cleared in the same `UPDATE` (otherwise it would be compared with a load from before the correction).
- **A box missing from the box repository is skipped by `VehicleLoadReader`**, which in the component tests means a
  ledger-only box is not part of the load. Seed both `InMemoryBoxRepository` and the convoy fake's ledger.
- **The tasks and the staleness read cost** one load read per vehicle (allocation, then box, items and Receiver per
  box). At roughly ten vehicles a convoy ([O23](domain/decisions.md#o23)) this is deliberate; do not cache it.
- **A stale declaration has no `ReadyToFile` state to fall back to.** `withdraw` starts a `Draft`; marking it ready (or
  recording a reference) snapshots the load again.

## 6. Testing

### Reqnroll matches step text globally

A step defined in two `[Binding]` classes is an **ambiguous binding error**, not an override. This
broke an existing Vehicles scenario when a duplicate was added to `ConvoysSteps`. Generic HTTP
steps therefore live in `ApiSteps` and per-slice classes hold only their own vocabulary.

### The BDD harness tracks created resources by `Location` header

Not by request body, because `/people`, `/convoys`, `/boxes` and `/receivers` all have
server-minted identifiers. `ScenarioState` also offers `Remember`/`Recall` (pin the last created
key under a name) and `Pin`/`Pinned` (store an arbitrary value) for scenarios that create more
than one thing.

Cleanup deletes as `admin` **except receivers**, which are deleted as `groundofficer` — an admin
token is correctly refused there, and a hook that silently 403s would leave delivery detail
behind.

### A test double must be exactly as strict as the thing it replaces

The vehicle inspection "did not persist" for a whole feature while every test was green. The
web page sent `inspectionStatus` in the vehicle `PUT`; the C# request record had no such field,
System.Text.Json dropped it silently, and the zod read schema's `.default('Pending')` papered
over its absence on the way back. The MSW mock, meanwhile, *stored* the field — so the web tests
proved the page talked to a mock that did not exist. The same shape was in the backend:
`InMemoryConvoyRepository` returned `[]` where the SQL returned `null`, and kept crews the SQL
deleted.

The rules that came out of it:

- An MSW handler accepts **only** the fields the C# request declares, and applies the same rules
  (404 / 409 / 422 and their `detail` text). `web/src/test/msw/convoys.ts` takes the fleet and
  roster as lookups (`convoyApi(seed, { fleet, people })`) for exactly this reason.
- No `.default()` on a **response** schema: a field the API omits must fail parsing, loudly.
- A save test asserts on the store (`api.db`) *and* on what a fresh render reads back — not on
  the button still being on screen after the click.
- Every `InMemory*Repository` mirrors its SQL's keys, conditional `WHERE`s and clean-up.
- The end-to-end check that would have caught it is a Playwright spec that saves, **reloads**,
  and reads the value back (`e2e/vehicles.smoke.spec.ts`).

### Integration tests share one helper, connect as `freedom_app`, and can be made mandatory

`tests/UA.Action.Freedom.Tests.Integration/SqlTestDatabase.cs` replaced nine copies of
`ConnectOrSkipAsync`/`ExecuteAsync`/`ScalarAsync`. Three of those copies connected as `sa`, which
bypasses every grant — a repository test could pass against a permission `freedom_app` does not
hold (§3). `FREEDOM_REQUIRE_INTEGRATION=true` turns "database unreachable" from a skip into a
failure; the CI `acceptance` job sets it, because a skipped suite there means broken
infrastructure, not absent infrastructure.

### A domain 404 keeps its reason

The web client used to turn *every* 404 into `ApiNotFound("Not found")`, which the crew panel
then swallowed — "There is no vehicle with VIN … on this convoy" never reached the user. A 404
whose problem body carries a `detail` is now an `ApiDomainProblem`; a bare 404 (the resource
addressed does not exist) is still `ApiNotFound`, which detail pages render as "Not found".

### VIN and manifest keys must be sent as `varchar`

Dapper sends every .NET string as `nvarchar(4000)`. Under `SQL_Latin1_General_CP1_CI_AS` — the local
database's collation and Azure SQL's default — `WHERE Vin = @vin` against a `varchar(32)` column then
converts the **column**, so the key lookup becomes a scan that locks every row it reads. It was
invisible until the convoy-arrival transaction touched several vehicles at once and began
deadlocking with single-vehicle inspection updates (found in the `system_health` deadlock graph,
not in any exception). `SqlKey.Of(vin)` sends the value as `varchar(32)`; where a whole record is
the parameter object, the SQL casts the parameter instead.

### Running the suites in parallel is a concurrency test — keep it

`dotnet test --solution` runs the Integration and BDD projects at the same time against one
database. That is how the deadlock above surfaced, as a BDD scenario failing one run in two. A
flaky scenario there is worth a deadlock graph before it is worth a retry.

### Frozen manifests accumulate in the local database

BDD cleanup cannot delete them — `DELETE` on a frozen manifest is correctly a `409`, and the hook
is best-effort. Ids are unique per run so nothing collides, but the rows stay. `docker compose
down -v` clears it.

### Health probes are a poor subject for a telemetry test

`TelemetryTests` deliberately asserts on `/vehicles`, not `/health/live`: probes are filtered out
of tracing and of the HTTP metrics (§ Observability below), so a test built on one would fail for
the right reason and look like a regression. The 401 path is used on purpose — an authorization
problem in production has to be traceable too. `TelemetryRedactionTests` asserts the filtering
itself, with a real request as the positive control.

### Telemetry component tests must scope to their own trace

A tracer provider listens to `ActivitySource`s **process-wide**, so a test host also receives the
spans of every other host running in parallel. Send a `traceparent` with a known trace id and
assert only on that trace (`TelemetryRedactionTests.ClientTracedAs`). The response also reaches the
client slightly *before* ASP.NET Core stops the request's span, so wait for the span rather than
reading straight after the response — `TelemetryTests` was flaky one run in ten until it did.
Unit tests that read metrics use a `MetricCapture` bound to one `Meter` *instance* for the same
reason; never a listener keyed on the meter's name.

### Component tests assert on `JsonElement`

Never on the read model. The JSON contract is what is being pinned; deserialising into the type
under test only proves it agrees with itself.

### Queue message contracts are pinned as literal JSON

`GmrSubmissionProcessorTests` and `ManifestDocumentProcessorTests` both hold the wire shape as a
raw string. Round-tripping through the serialiser would keep passing while the producer wrote
camelCase and the consumer expected PascalCase. Both messages may also carry an optional
`traceparent` (§ Observability); it is absent on a message queued while nothing was being traced,
so an untraced message is byte-for-byte what it was before tracing existed.

### Storage clients are optional dependencies

`AddFreedomStorage` only registers `BlobServiceClient` / `QueueServiceClient` when a storage
account is configured, because the application is expected to start without one and explain
itself on `/health/ready`. Registering `AzureManifestWorkQueue` with `AddScoped<TService,
TImplementation>` broke **every** component test at once — DI validation fails at
`WebApplicationBuilder.Build()`. It is registered with a factory using `GetService` (not
`GetRequiredService`), and throws a message naming the missing setting if used.

---

## Observability (between §6 and §7)

How the three services describe themselves to Grafana, and what they are built never to say. The
wiring is `src/UA.Action.Freedom.Telemetry`; dashboards and the metric catalogue are in
`iac/local/grafana/`. Unnumbered so the section numbers everything else refers to stay put.

### Never set `service.namespace`

The OTLP-to-Prometheus mapping turns `service.namespace` into a prefix of the `job` label
(`freedom/freedom-app`), and every dashboard filters on `job = service.name`. Setting it in
`OTEL_RESOURCE_ATTRIBUTES` — or in code — silently empties every panel.

### What is never a tag, an attribute or a log field

Receiver address, contact or free-text `reason`; volunteer names, dates of birth, phone numbers;
plates and VINs; EORI and insurance detail; route stops; principal ids (`sub`); HMRC error bodies;
ICS2 MRNs and ELO envelope numbers, which are customs references tied to one consignment.
A metric tag must be a **bounded set** — a handler name, an outcome enum member, a `ManifestStatus`,
a fixed reason. `ManifestId` and the queue `MessageId` are allowed on spans and in log scopes (the
workers already log them) but never on a metric, where each would mint a series.
`GmrOutcomeCollector` maps HMRC's `state` through the `State` enum and reports anything else as
`unknown`, so a surprising payload cannot mint labels either.

### Redaction is a span processor, and its order matters

`RedactingActivityProcessor` rewrites spans in `OnEnd` — `http.route` is only known once routing
has run — so it must be registered **before** the exporter (`AddFreedomTelemetry` does this; the
exporter is added last). It keeps the route template and drops the concrete path and query on server
spans, reduces client `url.full` to `scheme://host:port`, and blanks any SQL statement that names the
`sensitive` schema (column names would describe the shape of the address data). Other statements
are kept — parameterised, so they hold no values — because a slow query is found by its text. The
ASP.NET Core instrumentation already replaces query *values* with `Redacted`; the processor drops
the query entirely, and the exposure it actually closes is the concrete **path**
(`/boxes/scan/{token}`, `/people/{id}`, a VIN).

### HMRC's exception messages carry response bodies

`GvmsApiException` and `PushPullNotificationsApiException` put up to 512 characters of HMRC's
response in their message (`ToString()` includes all of it), and that can echo a plate or an EORI.
The workers log the **status and exception type only** — `GmrSubmissionProcessor` and
`CustomsWorkerService.LogUnhandled` — pinned by tests that feed a body containing a name. Do not
"improve" a worker log line by passing the exception object. The cost: the known PPNS deserialisation
failure (§ "Known bug, not ours to fix cheaply") logs as `HMRC answered 200 (PushPullNotificationsApiException)`;
reproduce it locally to see the underlying message.

### Queue traces link; they do not parent

Queue Storage has no message headers, so `AzureManifestWorkQueue` writes the producer span's
`traceparent` into the JSON body, and each worker starts a **consumer** span with an `ActivityLink`
to it. A message that is retried is processed minutes after the request that produced it; a child
span would stretch the approval's trace across that gap. In Tempo, open the worker's
`process customs-work` span and follow its link. The property is optional and `JsonSerializerOptions.Web`
ignores unknown members, so old and new producers and consumers interoperate.

### Queue metrics carry logical queue names

Queue metrics carry the **logical** queue name (`customs-work`, `manifest-documents` — `QueueNames`),
never the configured storage-queue name, so a dashboard does not need to know the environment.
The class is `QueueFlowMetrics` because `QueueMetrics` collides with an Azure SDK type of that name.

### Probes are excluded twice, and old series linger

Health probes are dropped from tracing by the ASP.NET Core `Filter` in `TelemetryInstaller` and from
the HTTP metrics by `DisableHttpMetrics()` on the health endpoints. Prometheus keeps a stopped
container's series for the length of the query window, so after a redeploy `increase(...[10m])` on
`/health/live` still shows the *previous* container's probes — check `service_instance_id` before
concluding the exclusion failed.

### Metrics take up to a minute to appear

The SDK exports metrics every 60 s. A panel that is empty a few seconds after an event is not
broken. Observable gauges (`freedom_queue_depth`, `freedom_worker_loop_last_success_seconds`) report
only while the worker is running, and a queue that cannot be read reports *nothing* rather than a
stale number — an absent series is the signal.

### A worker loop that has stopped looks like a worker with nothing to do

Each loop reports `freedom_worker_loop_last_success_seconds` on every completed pass, idle or not, so
a stale heartbeat means stuck or dead. The customs worker's `outcomes` loop currently fails every
poll (the PPNS enum bug above), which shows as `freedom_worker_loop_errors_total{loop="outcomes"}`
climbing and no `outcomes` heartbeat at all. That is the telemetry working, not a telemetry fault.

### Errors carry a `traceId`

Every 400/500 body from the exception handler includes `traceId`. It is the *trace* id (what Tempo
searches on), not ASP.NET Core's request id, and falls back to that only when nothing is being traced.

### Azure SDK spans: `Azure.Storage.*` only

`AppContext` switch `Azure.Experimental.EnableActivitySource` makes the SDK emit a span per queue and
blob operation (`QueueClient.SendMessage`). The source pattern is `Azure.Storage.*` deliberately:
`Azure.Core.Http` would repeat every HTTP call that the HttpClient instrumentation already records,
doubling the spans. Add another `Azure.<Library>.*` source when another SDK is adopted.

### Unobserved instances

Metric classes (`FreedomMetrics`, `QueueFlowMetrics`, `CustomsMetrics`, …) have a static `Unobserved`
instance backed by a meter nothing listens to, and the processors/handlers take the real one as an
**optional trailing parameter**. That keeps every existing positional construction in the unit tests
compiling and removes null checks; production always gets the DI-registered one.

---

## 7. The operator UI (`web/`)

### It is served under `/app`, not `/`

The SPA's client routes (`/vehicles`, `/manifests`, …) are the same strings as the API's own
collection routes. Serving the SPA at `/` would mean a browser hard-navigating to
`https://host/vehicles` gets the JSON list, not the app. So `Program.cs` serves it under
`/app` (`MapFallbackToFile("/app/{*path}", "app/index.html")`), Vite builds with `base:
'/app/'`, React Router uses `basename="/app"`, and the Dockerfile copies `dist` to
`wwwroot/app`.

### `UseStaticFiles` must run *before* `UseRouting`

`StaticFileMiddleware` bows out the moment routing has selected an endpoint. With
`UseStaticFiles` after the (auto-inserted) routing, every `/app/assets/*.js` request was
claimed by the `/app/{*path}` fallback and served `index.html` — a MIME error and a blank
SPA. `Program.cs` now calls `app.UseStaticFiles()` and then `app.UseRouting()` explicitly.
`StaticFrontendTests.A_built_asset_is_served_as_a_file_not_the_index_fallback` pins it.

### The access token lives in memory only

A hard reload drops it; the SPA then bounces through Keycloak (`prompt=none` against the SSO
cookie) to get a fresh one. In a Playwright spec this means **never `page.goto` between two
SPA pages** — navigate by clicking links, or the reload loses both the token and the target
route. A spec that switches seed users calls `signIn` (which clears cookies first);
`e2e/auth.setup.ts` captures the SSO cookie per user so single-user specs skip the form.

A reload now **returns to the page it was on**: `RequireAuth` passes the in-app path to
`signIn`, it travels through Keycloak in the OIDC `state`, and `onSigninCallback` navigates the
router there (`auth/returnPath.ts` validates it — an in-app path only, never `//host` or `/\`).
Before, every reload landed on the dashboard.

### `Hosting__ServeStaticFrontend` — default on, a no-op without `wwwroot`

`dotnet run` and `dotnet test` have no `wwwroot`, so the static middleware and fallback do
nothing and the API is unaffected. The container image is the only build with a populated
`wwwroot/app`. `src/UA.Action.Freedom.Api/wwwroot/` is git-ignored so a stray local `vite
build` cannot change what the Component tests host.

### Vitest Browser Mode

Tests run in real Chromium (`npx playwright install chromium`). Two non-obvious bits of
config: `resolve.dedupe` + `optimizeDeps.include` for react / react-dom / react-query /
router / oidc (Browser Mode otherwise hands a second, empty React copy to libraries that do
`import React from 'react'`), and an explicit `afterEach(cleanup)` in `src/test/setup.ts`
(Browser Mode does not auto-clean the DOM across files, so a left-open modal or stacked
render poisons the next file's `getByRole`). MSW needs the committed
`web/public/mockServiceWorker.js`.

### The reason-gate modal is hand-rolled

`react-aria-components` was dropped: its `Modal` left `aria-hidden` on the app tree after a
`cleanup()`-while-open, and every later test's `getByRole` then found nothing. `ReasonModal`
is a plain controlled `<div role="dialog" aria-modal>` overlay.

### The receiver-detail read is deliberately un-cached

`web/src/api/receiverDetail.ts` is an isolated module (imported only by the Ground Officer
panel), uses **no React Query**, and every `revealReceiverDetail(ref, reason)` is a fresh,
server-audited round trip. The reason is collected in the modal and never enters the URL or
a query key. `ReceiverReadModel` has organisation and region only — list/detail code has no
address field to leak.

---

## 8. Decided, but not built

| Item | Where it is written down |
| --- | --- |
| **Receiver detail retention sweep.** `sensitive.ReceiverDetail.DeleteAfter` exists and is populated; nothing deletes expired rows. Wants a timer-triggered job. | §4.4.5 |
| **ELO status after creation.** `POST /enveloppe/recuperer` returns `statut` (`FERMEE` → `APPAIREE` → `EMBARQUEE` → `DEBARQUEE`) plus the pairing, boarding and landing timestamps. Nothing polls it, so a stored envelope stays `FERMEE` for ever and a dispatcher cannot see that a lorry boarded. Pull rather than the `ENV_NOT01` push callback, so §4.1 holds and no inbound endpoint is needed; the WireMock stub already exists. | §4.1 |
| **ELO modification.** An envelope may be amended while `FERMEE` and not yet paired (`POST /enveloppe/modifier`, declarations to add and to remove). Now worth having: declarations *can* change, because an ENS with a wrong non-amendable field is invalidated and refiled, and nothing propagates the new MRN to an envelope that already names the old one. | §5b |
| **System-to-system ICS2 submission.** Declarations are filed by hand in the EU Customs Trader Portal and the MRN is recorded. The STI speaks eDelivery AS4, which needs an always-on inbound access point §4.1 declines — so this waits on an ITSP behind `IEnsDeclarationStore`, not on code. | `docs/adr/0003`, `docs/schemas/ics2/onboarding.md` |
| **An EU-issued EORI for the declarant.** A GB EORI is not accepted for an ENS and Ukrainian Action is UK-established. **This blocks filing entirely**, by anyone, in any environment, and it is an administrative question rather than a technical one. | `docs/schemas/ics2/onboarding.md` §1 |
| **Short-lived user-delegation SAS for documents.** Documents are written to blob storage; nothing serves them yet. Never put a document URL in an email — link to an authenticated page that mints the SAS. | §4.3 |
| **Notification worker.** Driver allocation and manifest approval emails, via Mailpit locally and ACS in Azure. Not started. | plan increment 8 |
| **Blob versioning and soft delete.** Assumed by `BlobManifestDocumentStore`'s overwrite-on-save comment; not provisioned in `iac/`. | §4.3 |
| **Azure provisioning.** Nothing exists on Azure; `iac/` is a local simulation. Both SQL logins become managed identities with no password there. | §4.2, `iac/README.md` |

---

## 9. Open questions — these need a person

1. **Does publishing a truck list really close the convoy's vehicle list to additions?**
   Adding a vehicle afterwards is a `409`, inferred from `process.puml` ordering *Truck List
   Published → Manifest Proposed* plus key-concepts.md describing the list as "published so
   manifests can be proposed against it". **Not a quoted requirement.** If the charity actually
   adds trucks late, this is a one-line relaxation in `AssignVehicleToConvoyHandler`.

   The *departure* half of this is now answered rather than inferred: vehicles break down, so a
   published list still takes a withdrawal. See "Publishing a truck list closes it to additions,
   not to departures" above.

2. **Is `Customs:RouteId` really route-level configuration?**
   It is currently one value for the whole application. If convoys ever cross by more than one
   route it becomes a property of the convoy and moves out of config.

3. **Should the manifest document be a PDF?**
   It is plain text: deterministic, diffable and testable. A letterhead wrapper can come later
   without changing *what is on it*. Nobody has said whether a border officer needs something more
   formal.

4. **Manifest verification for border guards.** The QR-code-plus-signed-token proposal in §4.5 is
   explicitly not a decision. It would reduce the pressure to print sensitive detail, but needs a
   conversation with someone who has stood at a border.

5. **Closing the loop from the public website.** §5.1 — donor and driver applications currently
   arrive by email and get re-keyed. Adding an endpoint means the only anonymous write path into
   the system, so it needs rate limiting, spam protection and an explicit approval step.

6. **Always Encrypted on receiver columns.** §4.4.4 suggests weighing it against key-management
   complexity. Not evaluated.

7. **What happens if the database auto-pauses mid-convoy?** Answered as "read-only from cached
   documents, revisit later" (§5.2 Q5). Nothing implements that fallback.

8. **Does the Customs Worker need to write GMR status back to the database?**
   `docs/c4/2-containers.puml` shows `customs_worker → db, "Writes GMR status against the
   Manifest"`, but neither `GmrSubmissionProcessor` nor `GmrOutcomeCollector` touch a database —
   only the work queue and `IGmrDocumentStore` (blob). `UA.Action.Freedom.CustomsWorker.csproj`
   has no reference to `UA.Action.Freedom.Data`. Relatedly, the database project
   (`database/UA.Action.Freedom.Database/Security/`) declares a `freedom_worker` role with DML
   grants, but `iac/local/sql/principals.sql` never creates a login/user for it —
   an orphaned role with nothing connecting as it. Two ways this resolves: either the diagram is
   simply wrong and GMR status is only ever readable from blob storage (matching the "pull-based,
   no inbound webhook" security posture elsewhere in this design), or a real feature is missing —
   the API/DB should be able to answer "has this manifest's GMR been submitted / what came back"
   without reaching into blob storage. Left unresolved rather than guessed; do not remove the
   `freedom_worker` role or the diagram edge until this is decided.

### Known bug, not ours to fix cheaply

**`HMRC.PushPullNotifications` cannot deserialise a notification.** HMRC sends
`"messageContentType": "application/json"`; NSwag generated the enum with `[EnumMember]` but
decorated the property with `JsonStringEnumConverter<T>`, which matches C# member names and
ignores `[EnumMember]`. Every response throws `JsonException`. **This affects real HMRC, not just
the local stub — do not "fix" it by changing the WireMock mapping.** The fix belongs in
`build/nswag/` plus a regeneration.

---

Questions 9–12 from the previous round are **decided and built**:

- **Erasing a volunteer named on old records** — split identity: the personal data is deleted,
  the anonymous key stays for the records, which read "Former volunteer". Refused while they are
  on a live crew or manifest team.
- **One driver on two vehicles of a convoy** — no: one seat per person per convoy, enforced by
  `UQ_VehicleDriver_Convoy_Person`. Passengers exist, and are any volunteer.
- **Crew changes after publication** — allowed. Removing a driver keeps the insurance in cover; a driver added
  needs it recorded again before departure. An arrived convoy's crew cannot change at all.
- **The 200-vehicle picker limit** — never reached: arrived vehicles are handed over and leave the
  picker for good, and a convoy is a handful of vans.

13. **Is "Returned" at arrival really "the vehicle came back"?** Arrival hands Delivered and Lost
    vehicles over; anything else is simply free for the next convoy. The question got sharper, not
    softer, when withdrawal was modelled explicitly: a vehicle that physically came back mid-journey
    is now *withdrawn*, which is a different fact from a *manifest* ending Returned. If Returned is
    only ever "the cargo came back", the two are independent and the handling is right; if it also
    means the vehicle came back, the two overlap and one of them is redundant. Needs a person.

14. **Should a withdrawn vehicle's manifest be re-issued when it joins a later convoy?**
    A manifest is bound to one crossing: `(ConvoyId, Vin)` is its identity, and its GMR named the
    original convoy's departure. A vehicle that is repaired and joins a later convoy therefore needs
    a *new* manifest against the new truck-list entry, and the old one stays as the record of the
    journey that did not finish. That is the safe reading of recommendations §5.2 — re-pointing a
    frozen manifest at a different crossing would tell HMRC one thing and do another — but it is an
    inference, and somebody at the charity should confirm the paperwork actually works that way.

15. **Worker retry and failure semantics** — found while instrumenting the workers, and now visible
    in the dashboards (`freedom_queue_redeliveries_total`, `freedom_gmr_dead_letters_total`), but
    deliberately not changed, because each is a behaviour decision:
    - `GmrSubmissionProcessor` dead-letters **every** HMRC 4xx — including 401/403 (an expired
      credential), 408 and 429 (a throttle). Those would succeed later; poisoning them loses the
      submission until someone re-queues it by hand. `freedom_gmr_dead_letters_total` is tagged with
      the status so this is at least countable.
    - Retries are **unbounded**. A message left for retry comes back every two minutes with no
      `DequeueCount` cap, and silently expires after Queue Storage's 7-day TTL — a manifest whose
      paperwork was never produced, with nothing recording that it gave up.
    - If HMRC accepts (202) and `CompleteAsync` then throws, the message is redelivered and the GMR
      submitted twice. `freedom_gmr_submission_duration_seconds` records that call as `accepted`,
      and the message as `left_for_retry`.
    - A dead-lettered message carries no reason; it exists only in a log line.
    - A non-JSON `BadHttpRequestException` (413, 415…) is mapped to a 500 by the exception handler,
      which inflates the 5xx panels with what are really client errors.
    - `/health/ready` is unauthenticated and returns exception messages, which can name hosts.

16. **Is OTLP straight to Application Insights actually supported?** The design and
    `AddFreedomTelemetry` assume `OTEL_EXPORTER_OTLP_ENDPOINT` can point at App Insights. Nothing in
    this repository verifies it. The alternatives are the Azure Monitor exporter or an OpenTelemetry
    Collector in front. The Grafana dashboards are PromQL/LogQL/TraceQL and do not port to App
    Insights (KQL / Workbooks); Azure Managed Grafana over Azure Monitor is the likely equivalent.
    Also unset until decided: sampling (`OTEL_TRACES_SAMPLER=parentbased_traceidratio` and
    `OTEL_TRACES_SAMPLER_ARG`) — the SDK default is 100%, so the "sampling from day one" in
    `recommendations.md` §2.4 is not yet true.

17. **Deleting a receiver something still names answers 500, after its address is already gone.** `DeleteReceiverHandler`
    removes the delivery detail through the Ground Officer identity first, then deletes `dbo.Receiver`. If a box
    (`FK_Box_Receiver`) or a vehicle (`FK_ConvoyVehicle_HandoverReceiver`, added by plan 04) still names the receiver, that
    second delete fails on the foreign key, nothing catches it, and the caller gets a 500 with the address already deleted
    and the receiver still there. The box half pre-dates registration; the handover receiver adds a second way to reach it.
    Every other delete maps a foreign-key refusal to a 409 `StillReferenced`. **Decide:** return `409` and check *before*
    removing the address (so a refused delete leaves the address alone), or accept deleting the address while the receiver
    stays. `GET /receivers/{ref}/usage` already says what names a receiver. Not fixed in plan 04.

18. **The fixed item categories exist only in the dev seed.** Plan 05 put the ten built-in categories in
    `database/seed/dev-seed.sql`, which is deliberately never run outside a local stack. A deployed database that was never
    seeded has no categories, so nothing can be packed until an Administrator adds one by hand, and they could not make one
    built in. **Decide:** make the list part of every deployment (a post-deployment script in the dacpac that inserts the fixed
    rows once, which the "no migration code" rule has to be read around), or accept that an Administrator creates them. The
    codes and shelf-life windows in the seed are placeholders either way (D25).


### Web coverage backlog

Pages with no test file of their own (some are exercised through a parent page's test):
`BoxBayPanel`, `BoxEditPage`, `BoxForm`, `BoxValidatePanel`, `ConvoyEditPage`, `ConvoyForm`,
`BaysPanel`, `LocationCreatePage`, `LocationEditPage`, `LocationForm`, `ManifestEditPage`,
`ReasonModal`, `ReceiverCreatePage`, `ReceiverEditPage`, `ReceiverForm`, `ReceiverDetailForm`,
`VehicleForm`; components `AppShell`, `ColdStartIndicator`, `DataTable`, `DetailCard`,
`NotAuthorized`, `NotFound`, `PageSkeleton`, `FormCard`. The API hook modules are covered only
through the pages. MSW handlers other than vehicles/convoys still cast request bodies with `as`
rather than parsing them against the contract.

---

## 10. Per-increment index

| # | Slice | The things worth remembering |
| --- | --- | --- |
| 1 | Domain remediation | `ManifestStatus` was a `record` with a `protected` constructor and *instance* properties returning `new` — unobtainable and unusable; replaced with an enum plus `ManifestTransitions`. All 27 `CS8618` warnings cleared — **keep the build at zero warnings**. Identity deliberately **not** standardised: each entity keeps the id type that matches how it is referenced. Misspellings all corrected in one commit. `Box.ValidatedAt` became nullable — it previously claimed every unvalidated box was validated at `0001-01-01`. |
| 2 | `/people` | Create returns a minted `Guid` rather than an outcome enum, because two volunteers can share a name and there is no conflict case. `Guid` not `IDENTITY` so a URL does not disclose how many volunteers the charity has. Removing the `/weatherforecast` template scaffolding broke two tests that were using it as a convenient unauthenticated route. The BDD harness was generalised here. |
| 3 | `/convoys` | The truck-list freeze (§9 Q1). `dbo.Vehicle.ConvoyId` became a real FK. `ReplaceRouteAsync` was the first transaction in the codebase (more followed — see §2) — a route is meaningful only as a whole journey. Stops are renumbered `1..n` in list order rather than trusting caller-supplied sequence numbers. The filtered-index/`QUOTED_IDENTIFIER` trap. |
| 4 | `/receivers` | The `sa` discovery (§3). Three layered controls. The audited read. `DELETE` behind `receivers:detail`. |
| 5 | `/boxes` | Validation is write-once and freezes the box. `boxes:validate` is separate from `boxes:write` — packing and vouching are different acts. The validator must be a volunteer on file; a signature naming nobody is worse than no signature. The 500 kg cap is a typo guard, not a real bound. |
| 6 | `/manifests` | The freeze semantics correction (§4). One `POST` per edge of the diagram, not a `PATCH` of a status field. `ConfirmAndFreezeAsync` as a single statement. Freeze-then-enqueue ordering. The optional-`QueueServiceClient` DI failure (§6). |
| 7 | Manifest worker | No database access at all, by design. Plain text output. The integration-test deadlock and its fix (§1). CI's `acceptance` job now starts both workers — it previously started only `app edge website`, so the queue hand-offs were never exercised there. |
| 8 | Operator UI (`web/`) | A React + Vite SPA co-served by the API under `/app` — see §7 for the load-bearing traps (`/app` not `/`, `UseStaticFiles` before `UseRouting`, in-memory token, Browser Mode config, the hand-rolled reason modal, the un-cached receiver-detail read). New `frontend` CI job (typecheck/lint/format/test/build) and Playwright `@smoke` specs in the `acceptance` job; `publish` needs both. A new public PKCE Keycloak client `freedom-spa` — the API's confidential client is unchanged. |
| 9 | Box QR labels | `dbo.BoxQrCode` — opaque non-enumerable token, revoke/reissue, revoked rows kept. `IssueQrCodeAsync` holds a transaction, same shape as the others in §2. "One active label" enforced there, not by a filtered index (§1). `QRCoder` is the first drawing dependency — managed renderers only, never the `System.Drawing`-based `QRCode` (§2). The label renderer's signature carries no receiver data, so the "no delivery detail on a travelling label" rule is structural (§3). New `App:PublicBaseUrl` env-only config with a request-host fallback; the local sim sets `App__PublicBaseUrl` because the container sees the edge, not the browser host. BDD probes `/boxes/scan/{all-zero-guid}` — auth runs before the handler, so a present route answers 401 and an old image answers 404. |
| 10 | Vehicle servicing + Mechanic | The inspection was silently dropped by the API and faked by the mock — see §6 "A test double must be exactly as strict". New `Mechanic` role and `vehicles:service` policy; `PUT /vehicles/{vin}/inspection` is the only writer. Convoy assignment gated on `Passed` in SQL, and no longer steals a vehicle from another convoy. Crew clean-up on convoy delete (a sixth transaction). Person delete 500 → 409. Schema guards for columns added after `CREATE TABLE`. Shared `SqlTestDatabase`, `freedom_app` everywhere, `FREEDOM_REQUIRE_INTEGRATION` in CI. Reload keeps the page (§7). |
| 11 | Crew, insurance, readiness, arrival, erasure | Passengers and one seat per person per convoy (`VehicleDriver` gained `ConvoyId` and `Role`, key `(ConvoyId, Vin, PersonId)`). Insurance per vehicle per convoy, voided by a crew change in the same transaction, gating `depart`. Advisory readiness as one pure function. Arrival hands vehicles over for good. Split volunteer identity for UK-data-protection erasure, with an in-place migration checked on a fresh SQL Server. Found and fixed: `nvarchar` VIN parameters scanning the table (deadlocks), and 500s deleting a vehicle or convoy a manifest names. |
| 13 | Convoy / manifest consolidation | The truck list became a table (`dbo.ConvoyVehicle`), and the manifest became its child: `(ConvoyId, Vin)` NOT NULL, composite FK, unique. `dbo.Vehicle.ConvoyId` removed — a pointer that arrival nulled, so an arrived convoy lost its own truck list. `dbo.ManifestDriverTeam` dropped; one crew record, now per journey leg, one seat per person per leg. Withdrawal added for the breakdown case: a stamp, not a delete. `IConvoyRepository` split in two. Found and fixed: the truck-list delete sat outside the `catch` that maps a foreign-key refusal to 409, and the GMR was being sent the VIN where its own comment said "plate". See `docs/adr/0001-truck-list-as-a-table.md`. |
| 12 | Observability | One shared wiring (`UA.Action.Freedom.Telemetry`) for all three services; the workers were previously not instrumented at all. Business metrics (command outcomes, manifest transitions, queue depth/age/dispositions, GMR reasons, worker heartbeats), queue trace *links*, and a span processor that keeps paths, queries, HMRC bodies and `sensitive`-schema SQL out of telemetry. Seven Grafana dashboards. Found and left for a decision: §9 Q14 (retry/dead-letter semantics) and Q15 (App Insights). See the *Observability* section. |
| 11 (plan) | Accommodation | `/convoys/{id}/tasks` is now a **mixed list**: every item has a `type` (`redeclare`, `accommodation-leftover`) and the web parses a discriminated union, so a new task kind needs a `type` and a `select` in `web/src/api/tasks.ts`. A route point a booking or flag stays at (**cancelled bookings included**) cannot be removed from the route: `AccommodationRoutePointReferences` answers `409 route-point-in-use`, and the composite foreign keys to `ConvoyRouteStop(ConvoyId, RoutePointId)` (hence its new unique constraint) refuse it for a race. Cancelling is a stamp, so a cancelled booking keeps its point and its cost is not counted in Hotel: a cancellation fee that stuck is an `Other` cost. Guests must be crewed when booked, but a guest already on a booking may stay when it is replaced, because the booking outlives them (P13). Erasing a guest stamps the identity `ErasedAt` (the guest row keeps pointing at it). `dbo.AccommodationBookingGuest` is exempt from the last-changed guard (a link; the booking is stamped). The Hotel line of the budget now reads bookings, so the plan 12 note that hotel actuals are not derived is out of date. The component fake `InMemoryAccommodationRepository` reads convoys and routes from the convoy fake and registers itself with it for the in-use check. |
| 12 (plan) | Budget, costs, equipment | Do not add a cost to the ferry or insurance tables twice: `BudgetPosition` reads bookings and policies, and `dbo.ConvoyCost` refuses anything but Fuel (0) and Other (4). Equipment counts under Other, so Other over budget may be equipment rather than a cost somebody entered; the summary's `equipmentGbp` says how much. Hotel actuals are not derived yet (plan 11 adds the bookings). `dbo.ConvoyCost.Vin` is deliberately not a foreign key: spent money must outlive a vehicle leaving the list, so the API checks the vehicle is on the convoy when the cost is entered. The component fake cannot cascade a convoy delete (it does not own convoys); `ConvoyBudgetRepositoryTests` proves the CASCADE. `InMemoryVehicleEquipmentRepository` hides a removed entry's lines rather than deleting them, so remove-then-re-add differs from the SQL. A new table needs `LastChangedBy`/`LastChangedAt` and a filtered index on `LastChangedBy` (see `LastChangedGuardTests`). |

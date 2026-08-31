# Gotchas and Open Questions

Things that will cost you time, rules that look like bugs but are not, and decisions that are
still outstanding. Written up while building out the API and workers across the domain, so most
entries are here because they actually bit somebody rather than because they seemed likely to.

**How to read this.** §1–§6 are things that are true now and will surprise you. §7 is work that
has been decided but not built. §8 is genuinely undecided and needs a person. §9 is a per-slice
index if you are looking for the history of one area.

Related: [`recommendations.md`](recommendations.md) for the hosting design and its reasoning,
[`domain/key-concepts.md`](domain/key-concepts.md) for the vocabulary, and `CLAUDE.md` at the
repository root for the working rules.

---

## 1. Tooling and environment

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

### sqlcmd runs with `QUOTED_IDENTIFIER` off

`iac/local/sql/001-schemas.sql` is applied by `sqlcmd`, which does not set `QUOTED_IDENTIFIER ON`.
Filtered indexes, indexed views and indexes on computed columns all require it, so
`CREATE INDEX ... WHERE ...` fails with `Msg 1934`.

There is a filtered index that *would* be worth having on `dbo.Vehicle (ConvoyId)` — most vehicles
are unassigned between convoys. It is deliberately unfiltered instead: a bootstrap script that
depends on the session settings of whatever tool invokes it is a trap, and the fleet is far too
small for the saving to matter.

### sqlcmd resolves `$(NAME)` from the environment

Used deliberately: `database.tf` forwards the two login passwords to the container with
`docker exec -e`, and the script references them as `$(FREEDOM_APP_PASSWORD)` and
`$(FREEDOM_SENSITIVE_PASSWORD)`. They never appear on a command line, in `docker inspect`, or in
the process table — the same reasoning as the `sa` password. See §3 of `iac/README.md`.

### Git Bash mangles container paths

`docker exec freedom-mssql /opt/mssql-tools18/bin/sqlcmd` from Git Bash becomes
`C:/Program Files/Git/opt/mssql-tools18/...`. Prefix with `MSYS_NO_PATHCONV=1`, or run it from
PowerShell.

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
that turns it into the shape the application uses. That is the exception, and it is commented as
such.

### The schema is one idempotent file, applied by hash

`iac/local/sql/001-schemas.sql` — hand-written T-SQL, no migration tool. `iac/tofu/database.tf`
re-runs it whenever `filesha256` changes, so **editing the file and running `tofu apply` is the
normal workflow**; there is nothing to taint.

Everything must therefore be re-runnable on a database that predates it — `IF OBJECT_ID(...) IS
NULL`, `IF COL_LENGTH(...) IS NULL`, `IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys ...)`.
Adding `FK_Vehicle_Convoy` also had to null out orphaned `ConvoyId` values first, because the
column existed as a loose `int` before `dbo.Convoy` did.

### Foreign key delete behaviour is deliberate, per relationship

| Relationship | Behaviour | Why |
| --- | --- | --- |
| `Vehicle → Convoy` | `ON DELETE SET NULL` | Vehicles *are* the aid. A cancelled convoy releases them; it must not delete donated vehicles. |
| `ConvoyRouteStop → Convoy` | `ON DELETE CASCADE` | A route has no life without its convoy. |
| `BoxItem → Box` | `ON DELETE CASCADE` | Items have no life outside their box. |
| `ManifestBox → Manifest` | `ON DELETE CASCADE` | Removes the *link*, not the box. |
| `ManifestBox → Box` | `ON DELETE CASCADE` | Deleting a box takes it off the manifest. |
| `Manifest → Vehicle` / `Convoy` | no action | Cannot delete a vehicle or convoy a manifest names. |
| `Box → Person` (validator) | no action | A volunteer who leaves must not take the record of what they signed for. |
| `ReceiverDetail → Receiver` | no action | Makes "delete the reference, keep the address" impossible. |

### `dbo.ManifestBox` is keyed on `BoxId`, not the pair

A box travels on **at most one** manifest. The same box on two manifests would be declared twice
at a border and arrive once. `AddBoxAsync` therefore *moves* a box rather than duplicating it.

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

### Receiver detail is protected three separate ways

Each holds if the others are removed by mistake:

1. **The `receivers:detail` policy** — GroundOfficer alone, not even Administrator. Administering
   access is not the same as holding it.
2. **A separate connection factory *interface*** — `ISensitiveDbConnectionFactory`, not a named
   lookup on the existing one. A repository asking for `IDbConnectionFactory` *cannot* be handed
   the Ground Officer connection, and widening that needs a constructor change a reviewer sees.
3. **The database `DENY`.**

`DELETE /receivers/{ref}` sits behind `receivers:detail` rather than `receivers:write`, because
removing a receiver removes its address.

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

### Publishing a truck list closes the convoy's vehicle list

Adding or removing a vehicle after publication is a `409`. **This is an inference, not a quoted
requirement** — see §8.

### A box cannot be changed after validation

No items in or out, no new receiver, no second validation. The Loader's confirmed weight is what
the border check relies on; any of those would leave it describing something no longer true.

### `Committed` requires `IsDriver`

Commitment is a commitment to *drive a leg*. Letting the two disagree would put a non-driver on
the dispatcher's committed-driver shortlist.

---

## 5. The write-once transition pattern

Three records are stamped once and then freeze their aggregate. They are all built the same way,
and a fourth should follow it:

| Record | Endpoint | Freezes |
| --- | --- | --- |
| `Convoy.TruckListPublishedAt` | `POST /convoys/{id}/publish-truck-list` | the convoy's vehicle list |
| `Box.ValidatedAt` + `ValidatedByPersonId` | `POST /boxes/{id}/validate` | the box's contents, weight and receiver |
| `Manifest.Status` + `GmrSubmittedAt` | `POST /manifests/{id}/approve` | the manifest's content |

The shape, in all three cases:

- **Absent from the request body and from the `UPDATE` statement.** There is no way to set, clear
  or forge them through an ordinary edit. Tests send those fields anyway and assert nothing moves.
- **The transition's SQL is conditional** — `AND TruckListPublishedAt IS NULL`,
  `AND ValidatedAt IS NULL`, `AND Status = @from` — so the *database* settles a race between two
  people pressing the same button, rather than a read-then-write in C#.
- **`ConfirmAndFreezeAsync` goes further** and does both in one statement with `OUTPUT INSERTED`.
  A manifest that is Confirmed but not yet frozen is editable, and that window is the thing §5.2
  rules out.

### Order of operations on approval

Freeze **then** enqueue, deliberately. A failed enqueue leaves a frozen manifest with no GMR —
visible, and an operator can retry. The reverse risks an editable manifest whose GMR is already on
its way, which is precisely what is forbidden. There is a `Received.InOrder` test on it.

---

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

### Frozen manifests accumulate in the local database

BDD cleanup cannot delete them — `DELETE` on a frozen manifest is correctly a `409`, and the hook
is best-effort. Ids are unique per run so nothing collides, but the rows stay. `docker compose
down -v` clears it.

### Health probes are a poor subject for a telemetry test

`TelemetryTests` deliberately asserts on `/vehicles`, not `/health/live`: probes are the first
thing anyone filters out of tracing, which would silently break the test later. The 401 path is
used on purpose — an authorization problem in production has to be traceable too.

### Component tests assert on `JsonElement`

Never on the read model. The JSON contract is what is being pinned; deserialising into the type
under test only proves it agrees with itself.

### Queue message contracts are pinned as literal JSON

`GmrSubmissionProcessorTests` and `ManifestDocumentProcessorTests` both hold the wire shape as a
raw string. Round-tripping through the serialiser would keep passing while the producer wrote
camelCase and the consumer expected PascalCase.

### Storage clients are optional dependencies

`AddFreedomStorage` only registers `BlobServiceClient` / `QueueServiceClient` when a storage
account is configured, because the application is expected to start without one and explain
itself on `/health/ready`. Registering `AzureManifestWorkQueue` with `AddScoped<TService,
TImplementation>` broke **every** component test at once — DI validation fails at
`WebApplicationBuilder.Build()`. It is registered with a factory using `GetService` (not
`GetRequiredService`), and throws a message naming the missing setting if used.

---

## 7. Decided, but not built

| Item | Where it is written down |
| --- | --- |
| **Receiver detail retention sweep.** `sensitive.ReceiverDetail.DeleteAfter` exists and is populated; nothing deletes expired rows. Wants a timer-triggered job. | §4.4.5 |
| **ELO generation.** The answer to §5.2 Q3 is that Freedom should generate it, but the API has not been identified — candidates are ENS, and France's NCTS/DELTA. Nothing is built. | §5.2 |
| **Short-lived user-delegation SAS for documents.** Documents are written to blob storage; nothing serves them yet. Never put a document URL in an email — link to an authenticated page that mints the SAS. | §4.3 |
| **Notification worker.** Driver allocation and manifest approval emails, via Mailpit locally and ACS in Azure. Not started. | plan increment 8 |
| **Blob versioning and soft delete.** Assumed by `BlobManifestDocumentStore`'s overwrite-on-save comment; not provisioned in `iac/`. | §4.3 |
| **Azure provisioning.** Nothing exists on Azure; `iac/` is a local simulation. Both SQL logins become managed identities with no password there. | §4.2, `iac/README.md` |

---

## 8. Open questions — these need a person

1. **Does publishing a truck list really close the convoy's vehicle list?**
   Implemented as a `409`, inferred from `process.puml` ordering *Truck List Published → Manifest
   Proposed* plus key-concepts.md describing the list as "published so manifests can be proposed
   against it". The reasoning: if a vehicle could leave afterwards, a manifest would go on
   describing a truck that is not travelling, and nobody would find out until loading day. **Not a
   quoted requirement.** If the charity actually adds trucks late, this is a one-line relaxation
   in `AssignVehicleToConvoyHandler`.

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

### Known bug, not ours to fix cheaply

**`HMRC.PushPullNotifications` cannot deserialise a notification.** HMRC sends
`"messageContentType": "application/json"`; NSwag generated the enum with `[EnumMember]` but
decorated the property with `JsonStringEnumConverter<T>`, which matches C# member names and
ignores `[EnumMember]`. Every response throws `JsonException`. **This affects real HMRC, not just
the local stub — do not "fix" it by changing the WireMock mapping.** The fix belongs in
`build/nswag/` plus a regeneration.

---

## 9. Per-increment index

| # | Slice | The things worth remembering |
| --- | --- | --- |
| 1 | Domain remediation | `ManifestStatus` was a `record` with a `protected` constructor and *instance* properties returning `new` — unobtainable and unusable; replaced with an enum plus `ManifestTransitions`. All 27 `CS8618` warnings cleared — **keep the build at zero warnings**. Identity deliberately **not** standardised: each entity keeps the id type that matches how it is referenced. Misspellings all corrected in one commit. `Box.ValidatedAt` became nullable — it previously claimed every unvalidated box was validated at `0001-01-01`. |
| 2 | `/people` | Create returns a minted `Guid` rather than an outcome enum, because two volunteers can share a name and there is no conflict case. `Guid` not `IDENTITY` so a URL does not disclose how many volunteers the charity has. Removing the `/weatherforecast` template scaffolding broke two tests that were using it as a convenient unauthenticated route. The BDD harness was generalised here. |
| 3 | `/convoys` | The truck-list freeze (§8 Q1). `dbo.Vehicle.ConvoyId` became a real FK. `ReplaceRouteAsync` is the only transaction in the codebase — a route is meaningful only as a whole journey. Stops are renumbered `1..n` in list order rather than trusting caller-supplied sequence numbers. The filtered-index/`QUOTED_IDENTIFIER` trap. |
| 4 | `/receivers` | The `sa` discovery (§3). Three layered controls. The audited read. `DELETE` behind `receivers:detail`. |
| 5 | `/boxes` | Validation is write-once and freezes the box. `boxes:validate` is separate from `boxes:write` — packing and vouching are different acts. The validator must be a volunteer on file; a signature naming nobody is worse than no signature. The 500 kg cap is a typo guard, not a real bound. |
| 6 | `/manifests` | The freeze semantics correction (§4). One `POST` per edge of the diagram, not a `PATCH` of a status field. `ConfirmAndFreezeAsync` as a single statement. Freeze-then-enqueue ordering. The optional-`QueueServiceClient` DI failure (§6). |
| 7 | Manifest worker | No database access at all, by design. Plain text output. The integration-test deadlock and its fix (§1). CI's `acceptance` job now starts both workers — it previously started only `app edge website`, so the queue hand-offs were never exercised there. |

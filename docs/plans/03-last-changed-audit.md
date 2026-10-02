# 03. Every entity records who last changed it

| | |
|---|---|
| **Branch** | `feat/last-changed-audit` |
| **Covers** | [ADR 0017](../adr/0017-every-entity-records-its-last-change.md), [O19](../domain/decisions.md#o19) |
| **Depends on** | [02](02-login-person-link.md) |
| **Gate** | None |

## Context

Nineteen tables exist (17 in `dbo`, 2 in `sensitive`). Seven have `CreatedAt`/`UpdatedAt`, written by seven
repositories (Box, Convoy, Location, Manifest, Person, Receiver, Vehicle). **None has an "updated by".** Purpose-built
records exist for a few acts: box validation, bay assignment, insurance and receiver-detail reads.

[ADR 0017](../adr/0017-every-entity-records-its-last-change.md) asks that **every entity records the person who last
changed it, and when**, taken from the login, in the same statement as the change. It is done early so that every
table later plans add is created with these columns.

### Decisions this plan relies on

| Decision | Effect here |
|---|---|
| [O19](../domain/decisions.md#o19) | `LastChangedBy` and `LastChangedAt` on every entity row. |
| [O34](../domain/decisions.md#o34), [O35](../domain/decisions.md#o35) | The person comes from `ICurrentPerson` ([plan 02](02-login-person-link.md)). An unlinked caller cannot write. |
| Volunteer erasure | A reference to an erased person reads "Former volunteer". |

## Increments

### Increment 1: a schema guard that enforces the rule

- **Tests (RED first):** a new integration test in `tests/UA.Action.Freedom.Tests.Integration/`. It queries
  `INFORMATION_SCHEMA.COLUMNS` and fails for any **entity** table missing either column.
- It holds an explicit allow-list of exempt tables, each with a one-line reason: pure link tables such as
  `ManifestBox`, and append-only logs such as `ReceiverDetailAccessLog`, which already record who.
- The test fails now, and passes at the end of the plan. Later plans inherit it.

### Increment 2: the columns

- **Schema:** each entity table under `database/UA.Action.Freedom.Database/dbo/Tables/` (and `sensitive/Tables/`
  where it applies) gains `LastChangedBy uniqueidentifier NULL` (FK to `dbo.Person`, no cascade) and
  `LastChangedAt datetime2 NULL`.
- They are nullable because seed rows and rows written before a login existed have none.
- Representative tables: `Convoy.sql`, `ConvoyVehicle.sql`, `Vehicle.sql`, `Box.sql`, `BoxItem.sql`, `Receiver.sql`,
  `Location.sql`, `Bay.sql`, `Manifest.sql`, `ConvoyRouteStop.sql`.
- Check that a second publish is a no-op.

### Increment 3: one display rule for a person who may be erased

- Today "Former volunteer" is a SQL `COALESCE(d.FirstName, N'Former')` / `COALESCE(d.LastName, N'volunteer')` in
  `src/UA.Action.Freedom.Data/Convoys/ConvoyVehicleRepository.cs` l.218–226, mirrored in
  `tests/UA.Action.Freedom.Tests.Component/InMemoryConvoyRepository.cs` l.396–399.
- Add a `dbo.PersonDisplay` view (`PersonId`, `DisplayName`) and grant `SELECT` on it to the app role in
  `Security/Permissions.sql`.
- Add a single in-memory helper the fakes share.
- **Tests:** an integration test that an erased person shows as "Former volunteer" through the view. Switch the crew
  query to the view.

### Increment 4: repositories stamp the caller

- Every `INSERT` and `UPDATE` in `src/UA.Action.Freedom.Data/*/…Repository.cs` sets both columns in the **same
  statement**, from a `changedBy` parameter.
- Conditional, write-once statements (`WHERE … AND TruckListPublishedAt IS NULL`, `ValidatedAt IS NULL`,
  `Status = @from`) set them too. Their meaning is unchanged.
- Handlers pass `ICurrentPerson`'s person through their commands.
- Go repository by repository, one commit each if the diff is large: Vehicle, People, Receivers, Boxes, Locations,
  Convoys (both repositories), Manifests.
- **Tests:** per repository, an integration test that an update stamps the caller. Update each `InMemory*Repository`
  in the same commit.

### Increment 5: show it

- Read models gain `LastChangedByName` (from `PersonDisplay`) and `LastChangedAt`.
- The web detail pages show "Last changed by … at …": vehicle, person, convoy, box, receiver, location and manifest.
- **Tests:** a component JSON contract test and a Vitest render test. An erased person renders as "Former volunteer".

## Retires and transitional

- **Retires:** the scattered "Former volunteer" `COALESCE`, which is replaced by one view.
- **Transitional:** `UpdatedAt` columns remain where they exist. Whether to drop them in favour of `LastChangedAt` is a
  choice recorded in the PR. Do not do both halves in this plan.

## Docs to update

- `CLAUDE.md`: an Architecture note that every entity has `LastChangedBy`/`LastChangedAt`, the schema guard test, and
  the `PersonDisplay` view.
- `docs/gotchas-and-open-questions.md`: the foreign key to `Person` means erasure stamps `ErasedAt` far more often.
- ADR 0017: an implementation note.
- `docs/domain/decisions.md`: the amendments table.

## Risks

- **Erasure path.** Most people will now be referenced by some row, so `PersonRepository.DeleteAsync` will usually take
  the "stamp `ErasedAt`" route rather than deleting the identity. Re-run `PersonRepositoryTests` and the BDD erasure
  scenario. The `UPDLOCK` and foreign-key fallback rely on `XACT_ABORT` being off on the app connection.
- **Statement count.** This touches every repository, so keep commits per repository and keep every gate green between
  them.
- **Re-publish.** Foreign-key and index names must be stable, or the no-op check fails.

## Testing

The schema guard (Integration), a stamping test per repository (Integration plus the fakes), Component contract tests,
and web render tests.

## Verification

The standard gates, plus: edit a convoy as `operator` (linked in plan 02), and its detail shows "Last changed by
<operator>". Erase that person, and it shows "Former volunteer".

## Sequencing

After [plan 02](02-login-person-link.md). Every later plan that adds a table relies on the schema guard.

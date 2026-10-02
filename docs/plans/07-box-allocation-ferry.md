# 07. Box allocation and ferry booking move to the truck-list entry

| | |
|---|---|
| **Branch** | `feat/box-allocation-ferry` |
| **Covers** | [ADR 0004](../adr/0004-the-manifest-is-the-load-sign-off.md) (cargo and ferry only); [D10](../domain/decisions.md#d10), [P1](../domain/decisions.md#p1) |
| **Depends on** | [03](03-last-changed-audit.md) |
| **Gate** | None |

## Context

A vehicle's cargo is `dbo.ManifestBox`, with its **primary key on `BoxId`** (one manifest per box), cascading from both
`Manifest` and `Box`. `ManifestRepository.AddBoxAsync` (l.186) moves a box between manifests with an UPDATE-then-INSERT
upsert. The document lines (l.286) and the ENS goods lines (l.323) **join `ManifestBox`**. The add and remove handlers
in `Application/Manifests/ManifestCompositionUseCases.cs` refuse with `ManifestBoxOutcome.Frozen` once the GMR stamp is
set.

The ferry booking is a boolean `FerryBookingComplete` on `dbo.Manifest`, also on the convoy create request
(`Api/Convoys/ConvoyRequests.cs` l.85), the update request, the web form and the read model.

ADR 0004 moves both to the **truck-list entry** (`dbo.ConvoyVehicle`). This plan does that move only. The manifest
lifecycle changes in [plan 15](15-manifest-signoff-lifecycle.md), and approval stops handing off in
[plan 08](08-declarations-filing.md).

### Decisions this plan relies on

| Decision | Effect here |
|---|---|
| [ADR 0004](../adr/0004-the-manifest-is-the-load-sign-off.md) | Cargo is a **box allocation to the truck-list entry**, keyed on `BoxId`, so a box is on at most one vehicle. |
| [P1](../domain/decisions.md#p1) | A ferry booking is **per vehicle, outbound only**, with a reference number and ticket details. |
| [D10](../domain/decisions.md#d10) | The capacity check (weight and volume) moves with the allocation and stays advisory. |

## Increments

### Increment 1: allocation in the domain

- `Domain`: a `BoxAllocation(ConvoyId, Vin, BoxId)`, and a rule that a box is on at most one entry. Moving a box
  replaces its allocation.
- **Tests (RED first):** unit tests for allocate, move and remove. Allocation to a **withdrawn** vehicle is refused.

### Increment 2: schema and repository

- **Schema:** a new `dbo.ConvoyVehicleBoxAllocation (BoxId PK, ConvoyId, Vin, AllocatedAt, LastChangedBy,
  LastChangedAt)`, with a foreign key to `ConvoyVehicle (ConvoyId, Vin)` and to `Box`. Cascade from Box. Use the
  same "only one cascade path" rule as ADR 0001.
- **Delete `dbo.ManifestBox.sql`.**
- **Application and Data:** `IConvoyVehicleRepository` gains `AllocateBoxAsync`, `RemoveBoxAsync` and
  `ListBoxesAsync`. Implement them in `ConvoyVehicleRepository`, keeping the move-not-duplicate behaviour.
- **Tests:** integration tests that a box can only be on one entry, and that a move is atomic. Mirror both in
  `InMemoryConvoyRepository`.

### Increment 3: API

- `GET|PUT|DELETE /convoys/{id}/vehicles/{vin}/boxes/{boxId}` under `boxes:write`, plus a list under `boxes:read`.
- **Transitional:** while the manifest still carries the GMR freeze, an allocation change on a vehicle whose manifest is
  frozen is refused with the same 409. That disappears in plan 15.
- Keep `GET /manifests/{id}/boxes` as a read through the allocation, so the manifest UI still works.
- `PUT/DELETE /manifests/{id}/boxes/{boxId}` return **410 Gone** with a pointer to the new route. Remove them in
  plan 15.
- **Tests:** component tests for the new routes, the policy, and the 410s.

### Increment 4: read paths follow the allocation

- In `ManifestRepository`, `GetDocumentLinesAsync` and `GetEnsGoodsLinesAsync` join
  `ConvoyVehicleBoxAllocation` instead of `ManifestBox`.
- `GetManifestWeightHandler` (`ManifestCompositionUseCases.cs` l.111–145) and the capacity check read allocations.
- **Tests:** unit and integration tests for weight and filing-sheet totals, unchanged in value.

### Increment 5: ferry booking per vehicle

- **Schema:** `dbo.ConvoyVehicleFerryBooking (ConvoyId, Vin, Operator, Reference, SailingAt, TicketDetails, CostGbp
  NULL, …)`, keyed on `(ConvoyId, Vin)`. `CostGbp` feeds [plan 12](12-budget-equipment.md).
- **Remove** `FerryBookingComplete` from `Manifest.sql`, the Manifest domain and read model, the update request, and
  the convoy create request.
- **Api:** `GET|PUT|DELETE /convoys/{id}/vehicles/{vin}/ferry` under `convoys:write`.
- **Tests:** all layers.

### Increment 6: web

- A new allocation panel on the convoy vehicle, replacing `web/src/pages/manifests/ManifestBoxesPanel` as the place
  where boxes are added. `ManifestDetailPage` shows them read-only.
- A ferry panel on the convoy vehicle.
- Remove the ferry checkbox from `ManifestForm.tsx`, `manifestModels.ts` and the convoy create form.
- Change `web/src/api/schemas/manifests.ts` (drop `ferryBookingComplete`).
- Tests: Vitest with MSW; `e2e/manifests.smoke.spec.ts`.

### Increment 7: BDD and seed

- `Manifests.feature`, `BoxDelivery.feature`: add boxes through the vehicle route, and book a ferry.
- Seed: allocations in place of `ManifestBox` rows, if the seed inserts any.

## Retires and transitional

- **Retires:** `dbo.ManifestBox`, the manifest ferry flag, and the manifest box write routes (410 now, removed in
  plan 15).
- **Transitional:** the GMR-stamp freeze still blocks allocation changes on a frozen vehicle until
  [plan 15](15-manifest-signoff-lifecycle.md).
- **Not changed yet:** `DeleteBoxHandler` still has no attested guard, so deleting a box cascades its allocation.
  [Plan 16](16-box-replacement-label.md) adds the guard.

## Docs to update

- `CLAUDE.md`: the Manifests and Convoys slices, the API list (new routes, removed routes), and the ferry booking.
- `README.md`: endpoints.
- `docs/domain/key-concepts.md` § Manifest: cargo is not on the manifest.
- Gotchas: `dbo.ManifestBox` "keyed on `BoxId`" becomes the allocation.
- ADR 0004: an implementation note.

## Risks

- **Two read paths join the old table.** Grep for `ManifestBox` across `src`, `tests` and `database` until nothing is
  left.
- **Cascade paths.** SQL Server allows only one cascade path to a child. Check the publish.

## Testing

All four .NET layers, the web panels, and BDD for adding boxes and booking a ferry.

## Verification

The standard gates, plus: `rg -n "ManifestBox|FerryBookingComplete" src tests web/src database` returns nothing except
the 410 handlers and their tests. On the local stack, allocate a box to vehicle A, then move it to B, and check A no
longer lists it. The filing sheet shows it on B.

## Sequencing

After [plan 03](03-last-changed-audit.md). Needed by [plan 08](08-declarations-filing.md) and
[plan 13](13-readiness-departure.md).

# 15. The manifest becomes the load sign-off

| | |
|---|---|
| **Branch** | `feat/manifest-signoff-lifecycle` |
| **Covers** | [ADR 0004](../adr/0004-the-manifest-is-the-load-sign-off.md) (lifecycle); [P6](../domain/decisions.md#p6), [X3](../domain/decisions.md#x3) |
| **Depends on** | [13](13-readiness-departure.md), [14](14-outcomes-closing.md) |
| **Gate** | None |
| **Flows** | [05 Load sign-off and declarations](../sequences/05-load-signoff-and-declarations.puml) ([process](../process/05-load-signoff-and-declarations.puml)), [06 Load change and re-declare](../sequences/06-load-change-and-redeclare.puml) ([process](../process/06-load-change-and-redeclare.puml)), [09 Delivery, acceptance and closing](../sequences/09-delivery-acceptance-closing.puml) ([process](../process/09-delivery-acceptance-closing.puml)) |

## Context

By now everything the manifest used to hold has another home:
- cargo and ferry ([plan 07](07-box-allocation-ferry.md));
- declarations ([plans 08](08-declarations-filing.md), [09](09-declaration-staleness.md));
- departure ([plan 13](13-readiness-departure.md));
- outcomes ([plan 14](14-outcomes-closing.md)).

What is left is the **sign-off**, still carried by the ten-state `ManifestStatus` (`Domain/Manifest.cs` l.113),
`ManifestTransitions.CanTransition` (l.156–176), the freeze on `GmrSubmittedAt`, and these **legacy readers of the
delivery states**:
- `ConvoyRepository.ArriveAsync` and `FinishedStatuses` (l.24, l.232, l.302);
- **volunteer erasure**, `PersonRepository.cs` l.24 and l.137, which refuses erasure while a person is on a vehicle whose
  load is not yet finished.

The web keeps nine verbs in `web/src/pages/manifests/transitions.ts`. `docs/manifest-status.puml` draws the ten states.

### Decisions this plan relies on

| Decision | Effect here |
|---|---|
| [P6](../domain/decisions.md#p6) | The manifest is the **sign-off plus the document pack**. Proposed, then Approved or Rejected. |
| [X3](../domain/decisions.md#x3) | **Any load change after approval needs re-approval**: the manifest returns to Proposed. This reverses "nothing may reopen a confirmed manifest". |
| ADR 0004 | **The freeze applies to the declared snapshot, not the load.** `GmrSubmittedAt` stops being the freeze signal. |

## Increments

### Increment 1: erasure and arrival tests first

- **Before changing anything,** write integration tests in `tests/UA.Action.Freedom.Tests.Integration/People/
  PersonRepositoryTests.cs` and `Convoys/ConvoyRepositoryTests.cs`:
  - erasure is refused while the person crews a convoy that has not arrived;
  - erasure is refused while they crew a vehicle whose boxes are not all finished;
  - erasure is allowed after;
  - arrival waits for every travelling vehicle to be delivered;
  - delivered vehicles are handed over.
- **"Finished" is now defined by outcomes:** a vehicle is finished when it is delivered (or withdrawn). Its boxes are
  finished when each is accepted, delivered, seized, returned to a hub or undeliverable.
- The tests are written against the outcome facts from [plan 14](14-outcomes-closing.md). They pass against the plan 14
  mirror, and must keep passing after the switch.

### Increment 2: switch the readers to outcomes

- Rewrite `ArriveAsync` and the erasure guard to read vehicle delivery and box outcomes instead of manifest statuses.
- **Keep `ArriveAsync`'s ordering:** take the convoy row first, and never scan `dbo.Vehicle` under a lock.
- **Tests:** the increment 1 tests stay green; update the fakes.

### Increment 3: the three-state lifecycle

- **Domain:** `ManifestStatus { Proposed, Approved, Rejected }`. `ManifestTransitions`: `Proposed → Approved | Rejected`,
  `Rejected → Proposed`, and `Approved → Proposed` **only through a load change**.
- `Created` folds into `Proposed`, unless the PR records a reason to keep a draft state.
- **Schema:** `Manifest.sql` changes its `Status` CHECK to `>= 0 AND <= 2`, and drops `GmrSubmittedAt`, since GMR
  state now lives on the declaration.
- **Tests:** rewrite `tests/UA.Action.Freedom.Tests.Unit/Domain/ManifestTransitionsTests.cs` edge by edge.

### Increment 4: re-approval on a load change

- An allocation change ([plan 07](07-box-allocation-ferry.md)), a box replacement or a box outcome that changes the load
  of an **approved** manifest moves it back to `Proposed`, in the same transaction as the change, using a conditional
  `UPDATE … WHERE Status = Approved`.
- Retire the plan 07 transitional 409 ("frozen"). Load changes are allowed, and they reopen the sign-off.
- **Tests:** unit, component and integration tests for each kind of load change.

### Increment 5: API and the routes plans 07 and 08 deprecated

- **Keep:** `POST /manifests/{id}/propose`, `approve` (Administrator) and `reject`.
- **Remove:**
  - `prepare`, `ready`, `deliver`, `lose` and `return` (`ManifestEndpoints.cs` l.228–236), since `depart` went in
    plan 13;
  - the 410 routes from plans 07 and 08 (`/manifests/{id}/boxes/{boxId}` writes and `/manifests/{id}/ens*`).
- `Frozen()` (l.307) and its message go.
- **Tests:** component tests for the remaining routes, and that removed routes return 404.

### Increment 6: web and diagrams

- `transitions.ts` keeps three verbs. `ManifestStatePanel.tsx` shows Proposed, Approved or Rejected, and "reopened by a
  load change" when that happened. Remove the frozen messaging from `ManifestDetailPage.tsx`.
- Update `docs/manifest-status.puml` and, if needed, `docs/process.puml`.
- Tests: Vitest, and `e2e/manifests.smoke.spec.ts`.

### Increment 7: BDD

- `Manifests.feature`: replace "approve freezes" and "frozen cannot be edited" with:
  - approval signs off;
  - a load change reopens it;
  - re-approval.
- Keep "convoy arrives and hands the vehicle over" via outcomes.

## Retires and transitional

- **Retires:**
  - the ten-state enum and the freeze on `GmrSubmittedAt`;
  - the delivery verbs and the deprecated routes;
  - the manifest-status mirror from plan 14;
  - `FinishedStatuses` as manifest states.
- Nothing is left transitional by the end of this plan.

## Docs to update

- [`docs/process/goods-movements.md`](../process/goods-movements.md) and `goods-movements.puml`: the freeze and the
  ten-state "Ready for departure" section. If plan 08 did not retire them, retire them now in favour of
  [process 05](../process/05-load-signoff-and-declarations.puml) and
  [process 06](../process/06-load-change-and-redeclare.puml).
- `CLAUDE.md`: the long "manifest lifecycle" and "frozen" paragraphs, the "three write-once records" paragraph, the API
  list, and the Domain model `ManifestStatus` paragraph.
- `README.md`.
- `docs/domain/key-concepts.md` § Manifest and § Manifest Status. Remove those rows from the decisions amendments table.
- Gotchas: §5.2 freeze notes.
- ADR 0004: an implementation note.

## Risks

- **Erasure is the most consequential reader.** A wrong "finished" lets someone be erased while still on the road, or
  blocks erasure for ever. That is why increment 1 writes those tests first.
- **Comments state the old freeze as law** in `ManifestReadModel`, the `Manifest.sql` header and
  `TransitionManifestHandler`. Rewrite them; do not leave them contradicting the code.

## Testing

Erasure and arrival (Integration, first), transitions (Unit), re-approval (all layers), routes (Component), the web,
and BDD.

## Verification

The standard gates, plus: `rg -n "GmrSubmittedAt|Preparing|InTransit|FinishedStatuses" src tests web/src database`
returns only the declaration's own GMR state. On the local stack: approve a manifest, move a box onto that vehicle,
and the manifest is Proposed again. Re-approve it. Erase a volunteer after their convoy closes, and it succeeds.

## Sequencing

After plans [13](13-readiness-departure.md) and [14](14-outcomes-closing.md). The end of the critical path.

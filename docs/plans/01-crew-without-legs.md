# 01. Crew without legs, and insurance that covers named drivers

| | |
|---|---|
| **Branch** | `feat/crew-without-legs` |
| **Covers** | [ADR 0007](../adr/0007-journey-legs-are-removed-from-the-crew-model.md); the insurance part of [ADR 0008](../adr/0008-readiness-is-computed-and-blocking-rules-are-not-overridden.md) ([O7](../domain/decisions.md#o7)); [P9](../domain/decisions.md#p9), [P12](../domain/decisions.md#p12) |
| **Depends on** | Nothing. May run before or after plan 02. |
| **Gate** | None |

## Context

A crew seat today is keyed by **leg**: `dbo.ConvoyVehicleCrew` has primary key `(ConvoyId, Vin, PersonId, Leg)` and
`UQ_ConvoyVehicleCrew_Convoy_Person_Leg (ConvoyId, PersonId, Leg)`. `JourneyLeg { Uk, Border }` runs through every
layer: readiness counts two drivers *per leg*, the crew API body requires `leg`, `DELETE …/crew/{personId}` takes a
required `?leg=`, the web crew panel is split by leg, and the seed cross-joins both legs.

The business rule is now simpler ([Convoy operations § Crew](../domain/convoy-operations.md#crew)): a seat is **one
person on one vehicle on one convoy**, one driver is required, and two are advised.

Insurance today is **voided by any crew change**: `ConvoyVehicleRepository.AssignCrewAsync` and `UnassignCrewAsync`
both call `VoidInsuranceAsync`, which stamps `ConvoyVehicleInsurance.VoidedAt`. The rule is now
([O7](../domain/decisions.md#o7)): **removing a crew member does not void it, and adding a driver needs it updated**.
The same code is rewritten for both changes, which is why they share a plan.

### Decisions this plan relies on

| Decision | Effect here |
|---|---|
| [P12](../domain/decisions.md#p12) | No `Leg` anywhere. One seat per person per convoy. |
| [P9](../domain/decisions.md#p9) | Readiness: one driver required, two advised. Readiness stays advisory until [plan 13](13-readiness-departure.md). |
| [O7](../domain/decisions.md#o7) | Insurance records **which drivers it covers**. Removing a driver keeps cover. An added driver is uncovered until the Dispatcher records the update. |

## Increments

### Increment 1: a crew seat has no leg

- **Domain:** delete `src/UA.Action.Freedom.Domain/JourneyLeg.cs`, and remove leg comments from `Driver.cs`.
- **Application:**
  - `Convoys/VehicleCrewUseCases.cs`: drop `Leg` from `AssignCrewToVehicleCommand`, `UnassignCrewFromVehicleCommand`
    and `ListVehicleCrewQuery`.
  - `Convoys/IConvoyVehicleRepository.cs`: drop `leg` from `ListCrewAsync`, `AssignCrewAsync` and `UnassignCrewAsync`.
  - `Convoys/ConvoyReadModel.cs`: replace `UkDriverCount`, `UkPassengerCount`, `BorderDriverCount`,
    `BorderPassengerCount` and `DriversOn(leg)` with `DriverCount` and `PassengerCount`. Remove
    `VehicleCrewReadModel.Leg`.
  - `Manifests/ManifestCompositionUseCases.cs`: the `ListCrewAsync(…, leg: null)` call becomes `ListCrewAsync(…)`.
- **Tests (RED first):**
  - `tests/UA.Action.Freedom.Tests.Unit/Convoys/VehicleCrewHandlerTests.cs`: assigning a person already on another
    vehicle of the same convoy is refused. A second seat on the same vehicle is refused.
  - Update `ConvoyTestData.cs`.

### Increment 2: schema and repository

- **Schema:** in `database/UA.Action.Freedom.Database/dbo/Tables/ConvoyVehicleCrew.sql`, drop the `Leg` column and
  `CK_ConvoyVehicleCrew_Leg`. The primary key becomes `(ConvoyId, Vin, PersonId)`, and `UQ_ConvoyVehicleCrew_Convoy_Person`
  goes on `(ConvoyId, PersonId)`. Rewrite the header comment.
- **Data:** in `src/UA.Action.Freedom.Data/Convoys/ConvoyVehicleRepository.cs`:
  - remove the per-leg `SUM/CASE` (around l.50–63);
  - change `ListCrewAsync` (l.198);
  - change `AssignCrewAsync` and `WhyNotSeatedAsync` (l.237–299). The "already on another vehicle" check is now per
    convoy;
  - change `UnassignCrewAsync` (l.313).
- **Tests:**
  - `Tests.Integration/Convoys/ConvoyVehicleRepositoryTests.cs`: rewrite the leg cases.
  - Fix the raw `INSERT … Leg` in `Tests.Integration/People/PersonRepositoryTests.cs:132`.
  - Fix the leg references in `ConvoyRepositoryTests.cs`.
  - Mirror the change in `Tests.Component/InMemoryConvoyRepository.cs`, which implements **both** convoy ports.

### Increment 3: API without `leg`

- **API:**
  - `src/UA.Action.Freedom.Api/Convoys/ConvoyRequests.cs:77`: `AssignCrewRequest(CrewRole? Role)`.
  - `ConvoyRequestValidators.cs:68`: remove the `Leg` rule.
  - `ConvoyEndpoints.cs`: GET crew loses `?leg`, and DELETE crew loses the required `leg` query parameter (l.226–303).
    Rewrite the error messages that mention legs.
- **Tests:** `Tests.Component/ConvoyEndpointTests.cs` (23 leg references) and `ManifestEndpointTests.cs` (5). Assert
  the JSON contract with `JsonElement`.

### Increment 4: readiness counts drivers per vehicle

- **Application:** in `Convoys/ReadinessUseCases.cs`:
  - remove `VehicleLegReadinessReadModel`, `Legs[]`, `AssessLeg` and the per-leg `Describe`;
  - replace `DriversNeeded = 2` with one driver required (a reason when missing) and two advised (a separate advisory
    reason).
- **Tests:** `Tests.Unit/Convoys/ConvoyReadinessTests.cs`:
  - one driver is ready, with the advisory reason;
  - zero drivers is not ready;
  - a withdrawn vehicle is still skipped.

### Increment 5: insurance covers named drivers

- **Schema:** a new `dbo.ConvoyVehicleInsuranceDriver (ConvoyId, Vin, PersonId)`, cascading from the insurance row,
  holds the drivers the policy covers. `ConvoyVehicleInsurance.VoidedAt` stays for an explicit void, and is no longer
  set by crew changes.
- **Data:**
  - **Delete `VoidInsuranceAsync`** (l.284–289) and its calls in `AssignCrewAsync` and `UnassignCrewAsync`.
  - `RecordInsuranceAsync` (l.353) records the covered drivers, defaulting to the current drivers.
  - Removing a driver also removes them from the covered list in the same transaction.
- **Domain and Application:**
  - `Domain/VehicleInsurance.cs`: `InCover` keeps its date rule. A new `CoversAllDrivers(drivers, covered)` states
    that every driver must be covered.
  - `Application/Convoys/InsuranceUseCases.cs`: the read model gains `UncoveredDrivers`.
- **Tests:**
  - Unit: adding a driver leaves them uncovered; removing one keeps the others covered; re-recording covers everyone.
  - Integration: the same three cases against SQL.
  - Component: `GET …/insurance` shows uncovered drivers.
- **Transitional:** `TransitionManifestHandler`'s `InTransit` check (`IsInsuredToday`) also requires every driver to be
  covered. [Plan 13](13-readiness-departure.md) moves this into departure.

### Increment 6: web

- `web/src/api/schemas/common.ts`: remove `journeyLegSchema`, `JourneyLeg` and `journeyLegLabels`.
- `web/src/api/schemas/convoys.ts`: change the crew row, `CrewAssignment` and the readiness schemas.
- `web/src/api/convoys.ts`: the crew PUT body and DELETE without `leg`.
- `web/src/pages/convoys/ConvoyDriversPanel.tsx`: one crew list per vehicle, and its test.
- `web/src/pages/manifests/ManifestCrewPanel.tsx`: change it, along with `ManifestPanels.test.tsx`,
  `ConvoyReadinessPanel.test.tsx`, `test/msw/convoys.ts` and `test/factories/convoy.ts`.
- Show uncovered drivers on the insurance panel.

### Increment 7: seed and BDD

- `database/seed/dev-seed.sql` l.84–89: remove the cross-join of legs, so no duplicate rows are produced.
- `tests/UA.Action.Freedom.Tests.BDD/Features/Convoys.feature` l.180–216: remove the `?leg=Uk` query and the
  `{ "leg": … }` bodies. Add a scenario: "adding a driver leaves the insurance needing an update".
- Change `Steps/ConvoysSteps.cs` and `Steps/ManifestsSteps.cs:80` to match.

## Retires and transitional

- **Retires:**
  - `JourneyLeg` and every per-leg read model, request field and query parameter;
  - `VoidInsuranceAsync`;
  - the rule that any crew change voids insurance.
- **Transitional:** readiness stays advisory, and the manifest `InTransit` check remains the only departure gate until
  [plan 13](13-readiness-departure.md).

## Docs to update

- `CLAUDE.md`:
  - the crew and insurance description in Architecture (`ConvoyVehicleRepository.AssignCrewAsync`/`UnassignCrewAsync`
    "void insurance");
  - the vehicle crew bullet in the Domain model;
  - the API line for the crew body.
- `README.md`: the crew endpoint body.
- `docs/local-authentication.md:204`: "per journey leg".
- `docs/gotchas-and-open-questions.md`: the `ConvoyVehicleCrew` `Leg` section and the unique-key notes (around
  l.198–211), and the commitment note (around l.385).
- `docs/domain/key-concepts.md` § Vehicle Insurance: confirm it matches.
- ADR 0007 and 0008: add an implementation note.
- `docs/domain/decisions.md`: remove the satisfied rows from *Consequences and amendments due*.

## Risks

- **`Convoys.feature.cs` is generated.** Rebuild the BDD project after editing the `.feature` file. Never hand-edit it.
- **Error semantics change.** "Already on another vehicle" is now per convoy. Check the UI message and any test that
  relied on a person being on two vehicles for different legs.
- **The fake must mirror the SQL exactly,** including removing a driver from the covered list.

## Testing

All four .NET layers, plus the web panel tests. BDD covers the crew API without `leg` and the insurance update path.

## Verification

The standard gates in the [execution protocol](README.md#6-gates-before-the-pr), plus:
- `rg -n -i "journeyleg|\\bleg\\b" src tests web/src database` returns nothing except unrelated words such as "legal".
- On the local stack, put a person on a vehicle and remove them, and check the insurance is still in cover. Add a
  second driver, and check the insurance panel shows them as uncovered.

## Sequencing

First in the series, or second after [plan 02](02-login-person-link.md). [Plan 10](10-route-points-convoy-leader.md)
and [plan 13](13-readiness-departure.md) depend on it.

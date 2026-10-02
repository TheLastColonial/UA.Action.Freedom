# 04. Receiver registration, and a vehicle's handover Receiver

| | |
|---|---|
| **Branch** | `feat/receiver-registration` |
| **Covers** | [ADR 0012](../adr/0012-receiver-registration-gates-convoys-and-boxes.md); [D22](../domain/decisions.md#d22), [D30](../domain/decisions.md#d30), [D33](../domain/decisions.md#d33), [D35](../domain/decisions.md#d35), [D36](../domain/decisions.md#d36), [P5](../domain/decisions.md#p5), [P11](../domain/decisions.md#p11) |
| **Depends on** | [03](03-last-changed-audit.md) |
| **Gate** | None |
| **Flows** | [11 Receiver registration](../sequences/11-receiver-registration.puml) ([process](../process/11-receiver-registration.puml)), [04 Convoy planning](../sequences/04-convoy-planning.puml) ([process](../process/04-convoy-planning.puml)), [02 Donation and box intake](../sequences/02-donation-and-box-intake.puml) ([process](../process/02-donation-and-box-intake.puml)) |

## Context

A Receiver today is `ReceiverReadModel(Ref, Organisation, Region)` in `dbo.Receiver`, with the address and contact in
`sensitive.ReceiverDetail` behind `receivers:detail`. **There is no status.** `CreateBoxHandler` relies on
`FK_Box_Receiver` and does not check the Receiver exists, so a bad GUID probably surfaces as an unhandled
`SqlException`. The web box form takes the receiver as **free text** (`web/src/pages/boxes/BoxForm.tsx` l.59–64).
Vehicles have **no handover Receiver**. Locations have **no "registered hub"** notion.

The rules are in [Boxes and donations § Receivers and destinations](../domain/boxes-and-donations.md#receivers-and-destinations).

### Decisions this plan relies on

| Decision | Effect here |
|---|---|
| [D30](../domain/decisions.md#d30), [D35](../domain/decisions.md#d35) | Statuses: pending, registered, suspended, expired. **Only `registered`** lets a box take the Receiver as destination, and a vehicle take it as its handover Receiver. |
| [D22](../domain/decisions.md#d22) and ADR 0012 | **The Administrator only** changes status. This is narrower than `receivers:write` (Administrator and Ground Officer). |
| [D33](../domain/decisions.md#d33) | **No field describes what kind of body a Receiver is.** The status records only that it may be sent to. |
| [D36](../domain/decisions.md#d36) | A hub is a `Location` an Administrator has registered. |
| [P5](../domain/decisions.md#p5), [P11](../domain/decisions.md#p11) | A vehicle has a handover Receiver, which must be registered before departure. Departure itself is enforced in [plan 13](13-readiness-departure.md). |

## Increments

### Increment 1: status in the domain

- `src/UA.Action.Freedom.Domain/Receiver.cs` gains `ReceiverStatus { Pending, Registered, Suspended, Expired }`, and a
  pure `CanReceive(status)` that is true only for `Registered`.
- New Receivers start as `Pending`.
- **Tests (RED first):** unit tests for `CanReceive`, and for the default status.

### Increment 2: schema, repository and status endpoint

- **Schema:** `dbo/Tables/Receiver.sql` gains `Status int NOT NULL DEFAULT 0`, with a CHECK in normalised form
  (`>= 0 AND <= 3`).
- **Application:** `Application/Receivers/ReceiverReadModel.cs` adds `Status`. `ReceiverReadModel` stays non-sensitive.
- **Data:** a new `SetStatusAsync` in `Data/Receivers/ReceiverRepository.cs`.
- **Api:**
  - A new policy `receivers:register`, **Administrator only**, in `Api/Configuration/AuthenticationExtensions.cs`.
  - `PUT /receivers/{ref}/status` in `Api/Receivers/ReceiverEndpoints.cs`.
- **Tests:**
  - Component: only an Administrator may change status. The Ground Officer and the Dispatcher get 403.
  - Integration: status round-trips.
  - Update `web/src/auth/policyMatrix.ts` and its pinning test.

### Increment 3: a box destination needs a registered Receiver

- `Application/Boxes/BoxUseCases.cs`: `CreateBoxHandler` and `UpdateBoxHandler` check that the Receiver exists and is
  registered.
- New outcomes: `ReceiverNotFound` (404 or 422) and `ReceiverNotRegistered` (409), instead of a foreign-key exception.
- **Tests:** unit and component tests for each outcome. Integration tests are unchanged.

### Increment 4: a vehicle's handover Receiver

- **Schema:** `dbo/Tables/ConvoyVehicle.sql` gains `HandoverReceiverRef uniqueidentifier NULL`, a foreign key to
  `dbo.Receiver`.
- **Api:** `PUT /convoys/{id}/vehicles/{vin}/handover-receiver`, under the convoy write policy. It is refused when the
  Receiver is not registered.
- Show it on `GET /convoys/{id}/vehicles`.
- **Tests:** unit, component and integration tests. Update the in-memory convoy fake.

### Increment 5: a registered hub

- **Schema:** `dbo/Tables/Location.sql` gains `IsRegisteredHub bit NOT NULL DEFAULT 0`, set by an Administrator
  under `locations:write`.
- **Tests:** component tests for the policy and the field.

### Increment 6: suspending a Receiver shows what it touches

- `GET /receivers/{ref}/usage` lists the convoys and boxes currently allocated to this Receiver: ids and counts only,
  never addresses. It is Administrator only.
- This lets the Administrator see which convoys a status change affects, as ADR 0012 asks.
- **Tests:** component tests, and a test that the response carries no address fields.

### Increment 7: web

- `web/src/pages/receivers/`: a status badge, and Administrator status controls on `ReceiverDetailPage`.
- `BoxForm.tsx`: replace the free-text receiver with a **picker of registered Receivers**, showing organisation and
  region only.
- The convoy vehicle panel: a handover Receiver picker.
- The location form: a registered hub checkbox.
- `web/src/api/schemas/receivers.ts` carries a "Do not add fields here" comment. Adding `status` is allowed because it
  is not sensitive. Update the comment to say why.
- Tests: Vitest with MSW, plus the `receivers.smoke.spec.ts` and `boxes.smoke.spec.ts` flows.

### Increment 8: seed and BDD

- `database/seed/dev-seed.sql`: add registered fictional Receivers (`dbo.Receiver` only, nothing in `sensitive`).
  Point seeded boxes at them.
- **BDD:** a new `Features/ReceiverRegistration.feature` covering:
  - an Administrator registers a Receiver;
  - a box cannot target a pending Receiver;
  - the Ground Officer cannot change status.

## Retires and transitional

- **Retires:** the free-text receiver on the box form, and the foreign-key exception path on box create and update.
- **Policy review:** `DELETE /receivers` sits under `receivers:detail` today, because deleting removes an address.
  Keep it, and record in the PR why it differs from `receivers:register`.
- **Transitional:** departure does not yet check the handover Receiver. That arrives with
  [plan 13](13-readiness-departure.md).

## Docs to update

- `CLAUDE.md`: the receivers policy list (`receivers:register`), the API list, and the segregation note that status is
  not sensitive.
- `README.md`: endpoints and the policy matrix.
- `docs/domain/key-concepts.md` § Receiver: registration status. Remove the row from the decisions amendments table.
- ADR 0012: an implementation note.
- `docs/local-authentication.md`: the policy matrix.

## Risks

- **[D33](../domain/decisions.md#d33).** Reviewers must check that nothing added describes the nature of a Receiver.
  Add a checklist line to the PR body.
- **Seed and BDD data.** Existing scenarios that create boxes must create a registered Receiver first.

## Testing

All four .NET layers, the web pickers and badges, and Playwright for the box form.

## Verification

The standard gates, plus on the local stack: create a Receiver, which is pending. A box cannot target it. Register it
as `admin`, and the box can. Log in as `groundofficer`, and the status control is absent and the API returns 403.

## Sequencing

After [plan 03](03-last-changed-audit.md). Needed by [plan 08](08-declarations-filing.md),
[plan 13](13-readiness-departure.md) and [plan 19](19-leader-address-access.md).

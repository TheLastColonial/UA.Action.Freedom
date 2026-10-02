# 12. Budget, actual costs and vehicle equipment

| | |
|---|---|
| **Branch** | `feat/budget-equipment` |
| **Covers** | [O12](../domain/decisions.md#o12), [O13](../domain/decisions.md#o13), [O37](../domain/decisions.md#o37), [P3](../domain/decisions.md#p3) |
| **Depends on** | [03](03-last-changed-audit.md), [05](05-item-classification-value.md) |
| **Gate** | None |
| **Flows** | [04 Convoy planning](../sequences/04-convoy-planning.puml) ([process](../process/04-convoy-planning.puml)), [09 Delivery, acceptance and closing](../sequences/09-delivery-acceptance-closing.puml) ([process](../process/09-delivery-acceptance-closing.puml)) |
| **Diagrams** | [Convoy operations (use cases)](../use-cases/convoy-operations.puml) |

## Context

There are no money fields on convoys. Insurance has an optional cost. [Plan 07](07-box-allocation-ferry.md) gives ferry
bookings a `CostGbp`, and [plan 11](11-accommodation.md) gives accommodation bookings one. The rules are in
[Convoy operations § Budget and costs](../domain/convoy-operations.md#budget-and-costs) and
[§ Vehicle equipment](../domain/convoy-operations.md#vehicle-equipment):
- a budget line per cost type, with actual costs against each;
- allocating the budget, and adding equipment to vehicles, are **steps in creating a convoy**;
- a budget is **not** required to depart.

### Decisions this plan relies on

| Decision | Effect here |
|---|---|
| [O12](../domain/decisions.md#o12), [P3](../domain/decisions.md#p3) | Budget lines: fuel, ferry, hotel, insurance, other. Actuals per line. Fuel actuals are entered by the Convoy Leader in [plan 18](18-leader-checklist-progress.md); until then by the Dispatcher. |
| [O37](../domain/decisions.md#o37) | No budget is an **advisory warning**, never blocking. |
| [O13](../domain/decisions.md#o13) | Vehicle equipment bought by the charity: accounted separately, no donor, **not part of the value delivered**. |
| [O25](../domain/decisions.md#o25) | The closing report ([plan 14](14-outcomes-closing.md)) reads budget against actuals. |

## Increments

### Increment 1: budget lines and actuals

- **Domain:** `CostType { Fuel, Ferry, Hotel, Insurance, Other }`, `BudgetLine(type, plannedGbp)` and
  `ActualCost(type, amountGbp, vin?, note, enteredBy)`.
- `Budget.Compare(lines, actuals)` returns, per line, the planned amount, the actual amount and a flag for over budget.
- Actuals for ferry, hotel and insurance are **derived** from the booking or policy cost, not entered twice.
- **Tests (RED first):** a unit table for `Compare`, including derived actuals and an over-budget line.

### Increment 2: schema and API

- **Schema:** `dbo.ConvoyBudgetLine (ConvoyId, CostType, PlannedGbp)` and `dbo.ConvoyCost (Id, ConvoyId, CostType,
  Vin NULL, AmountGbp, Note, …)` for the costs that are entered (fuel, other). Both carry audit columns.
- **Api:**
  - `GET|PUT /convoys/{id}/budget` (set lines);
  - `GET|POST /convoys/{id}/costs`, `DELETE /convoys/{id}/costs/{costId}`;
  - `GET /convoys/{id}/budget/summary`;
  - under `convoys:write`; read under `convoys:read`.
- **Tests:** component and integration tests. Add fakes.

### Increment 3: vehicle equipment

- **Schema:** `dbo.EquipmentItem` (a catalogue: name, unit cost, optional) and `dbo.ConvoyVehicleEquipment (ConvoyId,
  Vin, EquipmentItemId, Quantity, CostGbp NULL)`.
- **Api:** `GET|PUT /convoys/{id}/vehicles/{vin}/equipment`. Its cost counts under `Other`, or a dedicated `Equipment`
  type; pick one and record it in the PR.
- It is **excluded from the value delivered**: write a unit test that the value calculation (from
  [plan 05](05-item-classification-value.md)) ignores it.
- **Tests:** component and integration tests.

### Increment 4: creating a convoy includes both steps

- The web create-convoy flow becomes steps:
  1. details and route;
  2. truck list;
  3. **budget**;
  4. **equipment per vehicle**.
- Steps 3 and 4 can be skipped and returned to; nothing blocks.
- **Tests:** Vitest for the steps; a Playwright smoke test of creating a convoy with a budget.

### Increment 5: the budget warning

- Add the advisory items to the readiness read model ([P17](../domain/decisions.md#p17),
  [O37](../domain/decisions.md#o37)):
  - "no budget set";
  - "a line is over budget".
- Advisory only. [Plan 13](13-readiness-departure.md) restructures readiness, so keep this small and self-contained.
- **Tests:** unit tests.

### Increment 6: BDD

- `Features/Budget.feature`: set a budget, add a fuel cost, see a ferry cost derived from its booking, and see an over
  budget warning.

## Retires and transitional

- **Transitional:** fuel actuals are entered by the Dispatcher until the leader's checklist exists
  ([plan 18](18-leader-checklist-progress.md)).

## Docs to update

- `CLAUDE.md`: budget, costs and equipment in the Convoys slice, and the API list.
- `README.md`: endpoints.
- `docs/domain/key-concepts.md` § Convoy: budget and equipment.
- Remove the row from the decisions amendments table.

## Risks

- **Double counting.** A ferry cost entered on the booking and again as an actual would double the spend. Derive it;
  never enter it twice.
- **No reimbursement.** It is out of scope ([O10](../domain/decisions.md#o10)). Do not add payee or bank fields.

## Testing

`Budget.Compare` (Unit), persistence (Integration), API (Component), the web steps, and BDD.

## Verification

The standard gates, plus on the local stack: create a convoy through the steps with a £1,000 fuel line. Add £1,100 of
fuel costs, and the summary and the readiness warning both show over budget. A booked ferry with a cost appears as an
actual without being entered.

## Sequencing

After plans [03](03-last-changed-audit.md) and [05](05-item-classification-value.md). Needed by plans
[13](13-readiness-departure.md), [14](14-outcomes-closing.md) and [18](18-leader-checklist-progress.md).

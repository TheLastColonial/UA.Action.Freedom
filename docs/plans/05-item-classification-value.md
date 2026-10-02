# 05. Item classification, value and expiry

| | |
|---|---|
| **Branch** | `feat/item-classification-value` |
| **Covers** | [ADR 0014](../adr/0014-items-are-classified-by-category-and-valued-in-gbp.md); [D1](../domain/decisions.md#d1), [D5](../domain/decisions.md#d5), [D6](../domain/decisions.md#d6), [D7](../domain/decisions.md#d7), [D16](../domain/decisions.md#d16), [D21](../domain/decisions.md#d21), [D25](../domain/decisions.md#d25), [O11](../domain/decisions.md#o11), [O16](../domain/decisions.md#o16), [O31](../domain/decisions.md#o31), [O33](../domain/decisions.md#o33) |
| **Depends on** | [03](03-last-changed-audit.md) |
| **Gate** | None |
| **Flows** | [02 Donation and box intake](../sequences/02-donation-and-box-intake.puml) ([process](../process/02-donation-and-box-intake.puml)) |

## Context

An item today is `Item(Guid Id, string Description, Dictionary<string,string> Properties)` in the domain, and
`dbo.BoxItem` stores `Description`, `CommodityCode varchar(10) NULL` and `PropertiesJson`. The **domain `Item` has no
`CommodityCode`** although the database and read model do, and the **web drops it** (it is missing from
`web/src/api/schemas/boxes.ts`). `BoxRepository` maps `PropertiesJson` through a private `BoxItemRow` seam into a
`Dictionary<string,string>`. **The seed stores `{"quantity":10}`, a JSON number, which that deserialisation probably
rejects**: confirm, then fix. There is no category, value, currency or typed expiry anywhere. The ENS filing sheet
reports a blank `CommodityCode` in its `Missing` list (`ManifestEnsFilingUseCases.cs` l.237–274).

Vehicles store `PurchaserName` as free text and have **no price**.

### Decisions this plan relies on

| Decision | Effect here |
|---|---|
| [D6](../domain/decisions.md#d6), [O16](../domain/decisions.md#o16), [O31](../domain/decisions.md#o31) | A fixed category list plus free text. Each fixed category carries a hazard class, an expiry threshold and a code per authority. **The Administrator maintains the mapping.** |
| [D5](../domain/decisions.md#d5) | `ValueGbp` with `ValueSource` (`Donor` or `Estimate`). No currency conversion. |
| [D1](../domain/decisions.md#d1), [D25](../domain/decisions.md#d25) | A typed `ExpiresOn`. Already expired blocks the box. Short shelf life warns, at a threshold set per category. |
| [D16](../domain/decisions.md#d16), [D7](../domain/decisions.md#d7), [D21](../domain/decisions.md#d21) | A category is marked sensitive or not-carried. Adding a not-carried item warns at once. |
| [O11](../domain/decisions.md#o11), [O33](../domain/decisions.md#o33) | A vehicle's value is the price paid, or a GBP estimate with its source. |

## Increments

### Increment 0: confirm the seed defect

- Before changing anything, write an **integration test** that reads items written with
  `PropertiesJson = '{"quantity":10}'`.
- If it fails, as the code suggests, record the defect in the PR. The fix lands in Increment 7.

### Increment 1: domain value objects

- In `src/UA.Action.Freedom.Domain/`:
  - `ItemCategory` (id, English name, Ukrainian name, `IsFixed`, `HazardClass`, `IsSensitive`, `IsNotCarried`,
    `ShelfLifeRule`);
  - `ItemValue(decimal Gbp, ValueSource Source)`;
  - `ShelfLife.Assess(expiresOn, rule, asOf)`, returning `Expired`, `Short` or `Fine`.
- `Item` gains `CategoryId`, `Value`, `ExpiresOn` and `CommodityCode`. `Properties` stays for the rest.
- **Tests (RED first):** a unit table for `ShelfLife.Assess`: food at one third, medicine at half or six months
  (UNVERIFIED thresholds, held as configuration in the category rule), expired, and no expiry.

### Increment 2: reference data and mapping

- **Schema:**
  - `dbo.ItemCategory`;
  - `dbo.CategoryCustomsCode (CategoryId, Authority, Code)`, where `Authority` is UK, EU or UA.
- **Application and Api:** `Application/Categories/` with list, create, update and set-code commands.
  `ItemCategoryRepository` in Data. `GET /categories` for all operational roles. Writes under a new
  `categories:write`, **Administrator only** ([O31](../domain/decisions.md#o31)).
- **Seed:** fixed categories (medicine, food, clothing, hygiene, medical devices, tools, batteries, gas, flammables,
  other), with the not-carried flags on gas, lithium batteries and flammables ([D21](../domain/decisions.md#d21)).
- **Tests:** component tests for the policy, and integration tests for the round trip.

### Increment 3: items carry category, value, expiry and code

- **Schema:** in `dbo/Tables/BoxItem.sql`, add `CategoryId` (FK), `ValueGbp decimal(12,2) NULL`,
  `ValueSource int NULL` with a CHECK, `Quantity int NULL` and `ExpiresOn date NULL`.
- **Data:** `BoxItemRow` reads the typed columns. `PropertiesJson` keeps only what is left.
- **Application and Api:**
  - `Application/Boxes/BoxItemUseCases.cs` (`AddBoxItemCommand`), `BoxReadModel.cs` (`BoxItemReadModel`), and
    `Api/Boxes/BoxRequests.cs`.
  - The validator requires a category, a non-negative value with its source, and validates the code format.
- **Rules:**
  - Adding an **already expired** item is allowed, so the Loader can see it, but **validating** the box is refused
    while one is present ([D1](../domain/decisions.md#d1)).
  - Adding a **not-carried** item returns a warning in the response ([D21](../domain/decisions.md#d21)).
- **Tests:** unit tests for the validate refusal; component JSON contract tests; integration tests.

### Increment 4: the ENS filing sheet uses the mapping

- `ManifestRepository.GetEnsGoodsLinesAsync` (around l.300–335) takes the EU code from the category mapping, falling
  back to the item's own `CommodityCode`.
- The `Missing` list names an item whose category has no EU code.
- **Tests:** extend `tests/UA.Action.Freedom.Tests.Unit/Manifests/EnsFilingSheetHandlerTests.cs`.

### Increment 5: vehicle value

- **Schema:** `dbo/Tables/Vehicle.sql` gains `ValueGbp decimal(12,2) NULL` and `ValueSource int NULL`, where
  `Purchased` means the price paid and `Estimate` means it was given.
- Change `VehicleReadModel`, the requests, the validators, `web/src/api/schemas/vehicles.ts` and
  `web/src/pages/vehicles/VehicleForm.tsx`.
- **Tests:** all layers.

### Increment 6: web

- `web/src/pages/boxes/BoxItemsPanel.tsx` and `boxModels.ts`: a category picker, value with its source, quantity, an
  expiry date, and the commodity code (read only when it comes from the category).
- Show "expired" and "short shelf life" badges.
- An Administrator page for categories and their codes.
- Fix the Zod schema so `commodityCode` is kept.
- Tests: Vitest with MSW for each.

### Increment 7: seed and BDD

- `database/seed/dev-seed.sql` l.91–102: typed columns, categories and values. `PropertiesJson` uses string values only.
- **BDD:** `Boxes.feature`:
  - a box with an expired item cannot be validated;
  - a not-carried item warns;
  - the filing sheet shows the mapped code.

## Retires and transitional

- **Retires:** expiry, quantity and value held only in the property bag; the web dropping the commodity code; and the
  seed defect.
- **Transitional:** the value report itself arrives with [plan 14](14-outcomes-closing.md), and donor attribution with
  [plan 06](06-donors-donations.md).

## Docs to update

- `CLAUDE.md`: the Boxes and items description, the new policy, and the `BoxItemRow` seam note.
- `README.md`: endpoints and the policy matrix.
- `docs/domain/key-concepts.md` § Item and § Box.
- Gotchas: the `PropertiesJson` number issue, and that thresholds are unverified configuration.
- ADR 0014: an implementation note.
- `web/src/auth/policyMatrix.ts`.

## Risks

- **The thresholds are unverified** ([D25](../domain/decisions.md#d25)). Keep them as configuration on the category
  rule, not constants.
- **Category names will be translated** in [plan 16](16-box-replacement-label.md). Hold the Ukrainian name as a field,
  even if it starts empty.

## Testing

All four .NET layers, web panels, and Playwright for adding an item.

## Verification

The standard gates, plus on the local stack:
1. Seeded items load with no 500.
2. Adding an expired item and validating the box gives 409.
3. Adding gas warns.
4. The filing sheet shows the EU code from the category.

## Sequencing

After [plan 03](03-last-changed-audit.md). Needed by plans [06](06-donors-donations.md),
[08](08-declarations-filing.md), [12](12-budget-equipment.md) and [16](16-box-replacement-label.md).

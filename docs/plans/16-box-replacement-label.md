# 16. Attested boxes are replaced, and the label attests the contents in two languages

| | |
|---|---|
| **Branch** | `feat/box-replacement-label` |
| **Covers** | [ADR 0011](../adr/0011-attested-boxes-are-replaced-not-edited.md); [D2](../domain/decisions.md#d2), [D3](../domain/decisions.md#d3), [O17](../domain/decisions.md#o17), [O29](../domain/decisions.md#o29) |
| **Depends on** | [02](02-login-person-link.md), [05](05-item-classification-value.md), [09](09-declaration-staleness.md) |
| **Gate** | **Increment 0: translation spike and label data-sensitivity review.** Stop for the owner. |

## Context

A box is attested by `POST /boxes/{id}/validate`, which writes `ValidatedAt` and `ValidatedByPersonId` with a
conditional `UPDATE … WHERE ValidatedAt IS NULL` (`BoxRepository.ValidateAsync` l.122). After that, items cannot be
added or removed and the receiver cannot change. Two gaps:

- **An attested box can be deleted.** `DeleteBoxHandler` (`Application/Boxes/BoxUseCases.cs` l.79–92) has no
  validated guard, and the delete cascades through the allocation.
- **The label carries almost nothing.** `BoxLabelRenderer.ToSvg(int boxId, Guid token, DateTime issuedAt, string
  baseUrl)` (`src/UA.Action.Freedom.Api/Boxes/BoxLabelRenderer.cs`) draws a fixed 480×240 SVG: the charity name, box
  number, issue date and a scan hint. The redaction is structural: there is no parameter a receiver could reach.
  Tests assert the label never contains the receiver or location (`BoxQrCodeEndpointTests.cs` l.159,
  `BoxQrRendererTests.cs`, `BoxQrCodes.feature` l.59).

ADR 0011 replaces instead of edits, and puts the **item list and the signer** on the label in **English and
Ukrainian**. The Ukrainian is **machine translated with no external dependency**, and **not marked** as such
([O29](../domain/decisions.md#o29)).

### Decisions this plan relies on

| Decision | Effect here |
|---|---|
| [D3](../domain/decisions.md#d3) | Never edit an attested box: **void** it, create a replacement, copy the items, attest again. The old QR stops resolving. |
| [D2](../domain/decisions.md#d2), [O17](../domain/decisions.md#o17) | The label lists the items and the signer's name, in English and Ukrainian. Still **never** a Receiver, region or address. |
| [O29](../domain/decisions.md#o29) | Machine translation, **no external dependency**, **no marking**. Chosen by a spike. |
| ADR 0011 status | A **data-sensitivity review** of the new label is a gate. |

## Increment 0: the gate

**Produce both documents, commit them, open a draft PR labelled `awaiting-owner`, and stop.**

### Translation spike (time-boxed, at most one working day)

1. **Look for a Microsoft offline option first**: a Microsoft-provided translation runtime or language pack usable
   **offline, from .NET, inside a Linux container**, at no fixed cost. Record exactly what was found, with links,
   licences, platform limits and costs. If none exists, say so plainly.
2. **Fallback:** an open-source English→Ukrainian model (for example an OPUS-MT model) run **in-process** through
   `Microsoft.ML.OnnxRuntime`, with the model in the image. Measure image size, cold-start time, translation latency
   for a 20-line item list, CPU and memory, and the licences of the model and tokenizer.
3. **Prove "no external dependency"**: run the prototype with networking disabled and show it translates.
4. Compare against the hosting constraints in `docs/recommendations.md`: free allowances, scale to zero, and cold
   starts are accepted.
5. Write `docs/spikes/0011-offline-translation.md` with a recommendation.

### Label data-sensitivity review

Write `docs/security/0011-label-review.md`:
- what the new label shows: item categories and descriptions, quantities, the signer's display name, the box number,
  and the issue date;
- who may read it at each border;
- whether a signer's **full name** on a box that crosses borders is acceptable, or initials or a volunteer number
  should be used instead;
- whether any category names are themselves sensitive;
- confirmation that the receiver, region and address remain structurally impossible.

**Resume only after the owner signs off both**, with any changes they ask for recorded in ADR 0011.

## Increments

### Increment 1: an attested box cannot be deleted

- `DeleteBoxHandler` refuses with `AlreadyValidated` (409).
- **Tests (RED first):** unit and component tests. The BDD scenario "an attested box cannot be deleted".

### Increment 2: void and replace

- **Domain:** a `Box` gains `VoidedAt` and `ReplacesBoxId`. `Void` is terminal and allowed before departure.
- **Schema:** `Box.sql` gains `VoidedAt datetime2 NULL` and `ReplacesBoxId int NULL` (a self-reference with no cascade).
- **Data:** `ReplaceAsync(boxId)` runs in **one transaction**. It:
  1. voids the old box;
  2. revokes its active QR code, using the `IssueQrCodeAsync` pattern (l.218);
  3. creates the replacement with the items copied (category, value, donor, expiry);
  4. moves its allocation, if any.

  The replacement is **unattested**.
- **Api:** `POST /boxes/{id}/replace`, under `boxes:write`.
- **Tests:**
  - Unit: an unattested box cannot be "replaced", so edit it instead.
  - Integration: the transaction. The old token no longer resolves (`GET /boxes/scan/{token}` returns 404).
  - Component tests.

### Increment 3: voided boxes count once

- The value report, donor report, closing report, weight and declarations snapshot all exclude voided boxes.
- **Tests:** a unit test per calculation. A component test that replacing a box makes its vehicle's declarations stale
  ([plan 09](09-declaration-staleness.md)), and returns an approved manifest to Proposed if
  [plan 15](15-manifest-signoff-lifecycle.md) has merged.

### Increment 4: the translator port

- **Application:** `ILabelTranslator.TranslateAsync(IReadOnlyList<string> lines, "uk")`. A deterministic fake in tests.
- The adapter is the one the spike chose, in `src/UA.Action.Freedom.Api/Labels/`. Fixed category names use their
  stored Ukrainian name ([plan 05](05-item-classification-value.md)), and only free text is machine translated.
- **Tests:**
  - Unit: the fake in tests.
  - Integration: the real adapter, with **networking disabled** in the test container or process. Mark the test
    `Category=Integration`.

### Increment 5: the label shows the contents and the signer

- `BoxLabelRenderer.ToSvg` gains the **item lines** and the **signer's display name**, in English and Ukrainian, using
  the form the review approved.
- **The signature still takes no receiver, region or address parameter.** Layout grows as needed (multi-panel or a
  second page). Keep it deterministic.
- **Tests:**
  - **Keep** the no-receiver and no-location assertions in `BoxQrCodeEndpointTests` and `BoxQrRendererTests`, and the
    BDD scenario.
  - Add assertions for the item lines and signer in both languages.
  - The output is deterministic for a given input.

### Increment 6: web

- Box detail: a **Replace** action on an attested box, a "replaced by" and "replaces" link, and a voided badge.
- Change the label preview in `web/src/pages/boxes/BoxQrCodePanel.tsx`.
- Tests: Vitest with MSW; `e2e/boxes.smoke.spec.ts` for replace and print.

### Increment 7: BDD

- `Boxes.feature` and `BoxQrCodes.feature`:
  - replace an attested box, and the old QR stops working;
  - the label lists the items in both languages and still has no location.

## Retires and transitional

- **Retires:** deleting an attested box, and the "box number only" label.
- Rewrite the comments that say the label carries "nothing else": `BoxQrCodeReadModel` remarks, the `BoxQrCode.sql`
  header, `BoxLabelRenderer`, and `BoxQrCodes.feature`.

## Docs to update

- `CLAUDE.md`: Boxes and labels.
- `README.md`.
- `docs/domain/key-concepts.md` § Box and § Data Sensitivity (what a label carries now).
- ADR 0011: an implementation note, plus the spike outcome.
- Gotchas: translation model size and cold start.

## Risks

- **Image size and cold start.** An in-process model can add hundreds of megabytes. The spike must measure it against
  the scale-to-zero design.
- **Translation quality on a customs document.** The owner chose no marking. The review document should still record
  the risk that a mistranslation is read as attested.
- **No network, ever.** The integration test with networking disabled is what keeps "no external dependency" true.

## Testing

The delete guard, void and replace (all layers), excluding voided boxes (Unit), the translator (fake plus a
no-network integration test), the label (Component), web and BDD.

## Verification

The standard gates, plus on the local stack: attest a box and replace it. The old QR scan returns 404, and the new box
is unattested. Attest it, print the label, and check it lists the items in English and Ukrainian with the signer and no
location. Run the translator test with networking disabled.

## Sequencing

After plans [02](02-login-person-link.md), [05](05-item-classification-value.md) and
[09](09-declaration-staleness.md). Increment 0 may start earlier, in parallel with other plans.

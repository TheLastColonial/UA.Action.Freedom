# 11. An attested box is replaced, never edited, and its label attests the contents

Date: 2026-10-02

## Status

Accepted. **Implemented by [plan 16](../plans/16-box-replacement-label.md), except machine translation**, which is
deferred (see the implementation note). **Reverses a documented rule about the label.** The data-sensitivity review is
[`docs/security/0011-label-review.md`](../security/0011-label-review.md) and was signed off on 2026-10-04.

## Context

A box's contents are a **commitment the charity makes at border checkpoints**: a Loader has verified, weighed and
sized them and put their name to it. Today that is `Box.ValidatedAt` and `ValidatedByPersonId`, stamped once by
`POST /boxes/{id}/validate` and absent from the `UPDATE`, so nothing can set, clear or forge it. Once validated, a
box takes no new item and releases none, and its receiver cannot change.

That is the right rule, and it leaves two gaps that product discovery opened:

- **Contents sometimes must change.** An item turns out to have expired, a customs refusal removes one, a breakdown
  forces a re-split. The existing answer is "you cannot", which is not an answer a Loader can use.
- **The label carries the least it can.** The QR label contains a box number, a token and the charity's name, and
  deliberately nothing else, because it travels with the box and may be inspected. The attestation, *what is in
  the box and who vouched for it*, is not on the thing a border official sees.

## Decision

### An attested box is never edited. If its contents must change, the box is replaced

The old box is **voided**. A **new** box is created, its items copied across, and it is attested afresh by a
Loader. The old box's QR label **stops resolving** ([decision D3](../domain/decisions.md#d3)). Nothing is
unlocked, no attestation is ever cleared, and the record of what was attested and by whom is never rewritten.

`Void` is a terminal state reachable from any state before departure. The replacement points back to the box it
replaced, so the lineage is visible.

### The label carries the item list and the signer, in English and Ukrainian

The printed label **lists the items in the box and the name of the person who signed what was inside**
([decision D2](../domain/decisions.md#d2)), in **English and Ukrainian** ([decision O17](../domain/decisions.md#o17)).
**It still never carries a Receiver, a region or an address.** That part of the rule is unchanged, and it is
enforced by the type: `BoxLabelRenderer` takes only what may appear, so there is no parameter through which a
receiver could reach it.

## Alternatives considered

**Unlock and re-validate.** Rejected. It keeps one box, but clears an attestation, and "who attested this, when"
becomes a claim that can be rewritten, which is the thing the write-once stamp prevents.

**Edit with an audit log.** Rejected for the same reason: an audit trail records a rewrite and does not prevent
one, and a border official holding the original label would be holding a lie.

**Leave the label as it is, and print the list separately.** Plausible, and the two-label option was considered. It
was declined because the point of the QR label is that the **physical box carries its own attestation**.

## Consequences

**The label now carries data.** An item list is not an address, but it is read by border officials, and a list of
medicines, tools and vehicle parts is information about the load. The rule that a label never carries a Receiver,
region or address must keep its tests: the component and BDD tests that assert the rendered label never contains a
box's city, street or receiver reference stay, and gain assertions for the new content.

**The Ukrainian text needs a source.** Fixed categories can be translated once. A free-text category or an item's
own description cannot, until someone translates it
([Q-label-ukrainian-text](../domain/decisions.md#q-label-ukrainian-text)).

**Replacing a box ripples.** Its declarations go stale, the vehicle's load changes, and the manifest needs
re-approval ([ADR 0004](0004-the-manifest-is-the-load-sign-off.md),
[ADR 0005](0005-declarations-are-per-vehicle-with-derived-staleness.md)).

**A voided box must not count twice.** The new box carries the same items, so the value report
([ADR 0014](0014-items-are-classified-by-category-and-valued-in-gbp.md)) and the donor status report must exclude
voided boxes, or they double-count.

**Donor attribution, value and category are copied with the items,** so the replacement is the same donation
described again, not a new one.

**`BoxQrCode` already has the shape this needs.** Issuing a code revokes the active one in one transaction. Voiding
a box revokes its code the same way, in the same transaction as the void.

## Amendments

Added 2026-10-02, from questions resolved before implementation planning.

- **The Ukrainian text is machine translated with no external dependency, and is not marked as a translation**
  ([decision O29](../domain/decisions.md#o29)). That resolves "The Ukrainian text needs a source" above.
- **Two gates come first** in [plan 16](../plans/16-box-replacement-label.md): a time-boxed spike that picks the
  offline translation option (a Microsoft offline option first, otherwise an open-source model run in-process), and the
  label's data-sensitivity review. The plan stops for the owner's sign-off on both before changing the label.

## Implementation note (plan 16, 2026-10-04)

The owner signed off the spike and the label review with these decisions:

- **The label lists one line per item (category and quantity, and expiry where there is one), in English and
  Ukrainian**, and names the signer by **both a signer code and first name with last initial**. Never properties, value,
  donor, donation or free text. Medicine appears as its category. An erased volunteer reads "Former volunteer" and keeps
  the code. A printed name cannot be erased from a box that has shipped: accepted, and recorded as
  [Q-label-signer-erasure](../domain/decisions.md#q-label-signer-erasure).
- **The renderer takes a purpose-built `BoxLabelContent`, never a box or item read model.** Accepted as "not sure but
  yes" and flagged for revisit ([Q-label-renderer-type](../domain/decisions.md#q-label-renderer-type)).
- **There is no Microsoft built-in offline translation** ([spike](../spikes/0011-offline-translation.md)). Nothing is
  built or chosen: the Ukrainian is the category's `NameUk`, edited by an Administrator, and a machine suggestion
  confirmed by the attesting Loader is a seam that is not wired. Where a translator would run and which licence notice it
  needs are open ([Q-label-translation](../domain/decisions.md#q-label-translation),
  [Q-label-translator-licence](../domain/decisions.md#q-label-translator-licence)). The plan's Increment 4 is deferred.
- **Replace** is `POST /boxes/{id}/replace`. It is refused for a box nobody has validated, for one already voided, and
  (transitionally, until plan 15) while the vehicle's GMR exists. A voided box takes no label and no allocation.
- The fixed Ukrainian wording on the label ("Вміст", "Перевірено", "Не перевірено") was written without a translator and is
  for a Ukrainian speaker to confirm.

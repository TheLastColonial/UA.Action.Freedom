# 14. Items are classified by category, which maps to customs codes, and valued in GBP with a recorded source

Date: 2026-10-02

## Status

Accepted. Not yet implemented.

## Context

An item today is a `Description` and an open-ended bag of properties. The persistence layer shows how open:
`dbo.BoxItem.PropertiesJson` is a string that a private `BoxItemRow` seam in `BoxRepository` maps to a dictionary,
the one place in the data layer where the CLR type does not line up with the column.

Several decisions now need things the bag cannot give reliably:

- **Declarations need a commodity code** per item for the GMR, ENS and Ukraine's goods list, and the ENS filing
  sheet already reports "an item with no commodity code" in its `Missing` list
  ([ADR 0003](0003-ens-declaration-recorded-not-submitted.md)). The Ukrainian code is **UKTZED**, which differs
  from the UK and EU codes.
- **Expiry and shelf life have legal thresholds** ([D1, D25](../domain/decisions.md#d1)), so an expiry date has to be
  a date and not a string in a bag.
- **A value report** needs a value per item with a known source, because it goes to HMRC and auditors, the Charity
  Commission and the public ([decision D29](../domain/decisions.md#d29)).
- **Sensitive and prohibited goods** are found by category ([D16, D21](../domain/decisions.md#d16)).

## Decision

### An item has a category, and the category carries the rules

A **Loader classifies each item by choosing its category** ([decision O16](../domain/decisions.md#o16)). The category
list is **fixed, with free text for a new category** ([decision D6](../domain/decisions.md#d6)). A fixed category
carries its **hazard class**, **expiry threshold** and **commodity codes**. A free-text category carries none until
someone adds it to the mapping.

### Categories map to each authority's code

Each category maps to **the code used in each declaration**, so a category is classified once and the codes follow.
A Loader is never asked for a UKTZED code, and a Dispatcher never retypes one. The mapping is maintained
centrally, by an Administrator
([Q-admin-assignments](../domain/decisions.md#q-admin-assignments)).

### Value is an estimate in GBP, with a source

Every item has a **value in GBP**, and **the source is recorded**: `Donor` stated, or `Estimate`
([decision D5](../domain/decisions.md#d5)). There is **no currency conversion**. A value in USD or PLN is converted
by the person entering it, because the charity declined to pay for an FX service.

### Vehicles are valued at what was paid

A vehicle's value is **the price paid for it**, and the value report includes vehicles
([decision O11](../domain/decisions.md#o11)). A vehicle that was given, not bought, is open
([Q-vehicle-without-price](../domain/decisions.md#q-vehicle-without-price)).

### Equipment the charity buys is outside the value delivered

Vehicle equipment, such as warning triangles, is accounted for separately, has no donor and is not part of the value
delivered ([decision O13](../domain/decisions.md#o13)).

## Alternatives considered

**Ask the Loader for each code.** More accurate per item, and the wrong place for the work: a Loader packing a box
is not the person who knows three tariff schedules, and the codes would be re-entered for every item.

**Keep category as a property in the bag.** The property bag stays, for the truly per-item attributes (colour,
size and the rest), but a category that drives rules cannot be a free string that varies by spelling.

**Store values in the donor's currency and convert.** It needs an exchange rate source, a rate date and a rule for
which to use. Declined as a cost for no gain.

## Consequences

**Some properties become typed columns.** Category, value with its source, quantity and expiry are promoted out of
the bag, and the rest stay in it. The `BoxItemRow` seam stays for what remains.

**A filed declaration must record the codes it used.** If the mapping changes after a declaration is filed, the
filed declaration must not silently change. The declaration's snapshot includes the codes in force when it was
prepared ([ADR 0005](0005-declarations-are-per-vehicle-with-derived-staleness.md)), and a mapping change that
alters a filed code makes the declaration stale.

**An unmapped category blocks nothing by itself, and does show up.** It appears as an item with no code in the
filing sheet's `Missing` list and as an advisory warning ([P17](../domain/decisions.md#p17)), and it will block a
declaration being prepared, since a declaration cannot be written without a code.

**Reports depend on `Estimate` being honest.** A value report that mixes donor-stated and charity-estimated values
should say which, and the source is what makes that possible. Voided boxes must be excluded or the report
double-counts ([ADR 0011](0011-attested-boxes-are-replaced-not-edited.md)).

**The category list is data, and also a vocabulary.** The names are what appear on the label in English and
Ukrainian ([O17](../domain/decisions.md#o17)), so a translated name is part of a category's definition
([Q-label-ukrainian-text](../domain/decisions.md#q-label-ukrainian-text)).

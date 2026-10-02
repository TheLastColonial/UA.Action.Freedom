# Boxes and donations

The business rules for boxes, the items in them, who donated them, and where they go. Why each rule exists is in
[Decisions](decisions.md); the rules link to it.

See also: [Sequence diagrams](../sequences/README.md), [Process diagrams](../process/README.md), [Convoy operations](convoy-operations.md), [Customs declarations](customs-declarations.md),
[Key concepts](key-concepts.md).

## Purpose of a box

A box is the container of donated items sent to Ukraine. It must be:

- **weighed**, for border checkpoint checks;
- **sized**, so we can calculate how many boxes of a given cargo fit on a vehicle;
- **valued**, item by item, so the charity can report donation value for tax and public relations;
- **attested** by a Loader (a volunteer), because what is inside is a commitment the charity makes at border
  checkpoints.

## Box lifecycle

*Flows: [02 Donation and box intake](../sequences/02-donation-and-box-intake.puml) ([process](../process/02-donation-and-box-intake.puml)), [03 Box replacement](../sequences/03-box-replacement.puml) ([process](../process/03-box-replacement.puml)), [08 On the road](../sequences/08-on-the-road.puml) ([process](../process/08-on-the-road.puml)), [09 Delivery, acceptance and closing](../sequences/09-delivery-acceptance-closing.puml) ([process](../process/09-delivery-acceptance-closing.puml)).*

| State | Meaning |
|---|---|
| **Expected** | Ordered, or mailed in by a donor, and on its way to a distribution hub. A destination may already be set. Entering a box as expected is **optional**: it may be entered when it arrives ([O22](decisions.md#o22)). |
| **Arrived** | At a hub. May be shelved in a bay. |
| **Composed** *(optional)* | Built at the hub by combining several donations into one box. |
| **Attested** | A Loader has verified, packed, weighed and sized it, and a QR label is issued. **Contents are now fixed.** |
| **Allocated** | Assigned to a vehicle on a convoy, with a destination Receiver. |
| **Declared** | Its contents are covered by current [declarations](customs-declarations.md). |
| **Departed** | The vehicle carrying it has left. |
| **Delivered** | Marked arrived by the Convoy Leader or the Dispatcher ([O8](decisions.md#o8)). |
| **Accepted** | Ukrainian customs have accepted the delivery. Recorded once per goods list, for every box on it ([O9](decisions.md#o9), [O24](decisions.md#o24)). |
| **Seized** | Customs refused it at a border and kept it. Terminal ([O2](decisions.md#o2)). |
| **Returned to a hub** | Customs refused it at a border and it went back to the registered hub the Convoy Leader chose. It is then an arrived box again at that hub, with its label and contents unchanged, and may go on a later convoy ([O2](decisions.md#o2), [O28](decisions.md#o28)). |
| **Undeliverable** | Damaged or stolen, with the reason recorded. Terminal ([O5](decisions.md#o5)). |
| **Void** | Terminal. Reachable from any state before departure. A box is voided when it is replaced. |

## Items

*Flows: [02 Donation and box intake](../sequences/02-donation-and-box-intake.puml) ([process](../process/02-donation-and-box-intake.puml)).*

- An item carries a quantity and properties that vary per item: size, weight, colour, expiry and others.
- **Every item has a value in GBP.** The value is an estimate, and its **source is recorded**: stated by the donor, or
  estimated by the charity. Values in other currencies are converted by the person entering them
  ([D5](decisions.md#d5)).
- **Every item has a category.** Categories are a fixed list, and a new one may be added as free text. A fixed
  category carries a hazard class, an expiry threshold and a commodity code. A free-text category carries none until
  it is promoted into the list ([D6](decisions.md#d6)).
- **A Loader classifies each item by choosing its category.** Each category maps to the code used in each
  authority's declaration, so a category is classified once and the codes follow from it
  ([O16](decisions.md#o16)). An item in a free-text category has no code until the category is added to the mapping.
  **The Administrator maintains the mapping** ([O31](decisions.md#o31)).

## Donations and donors

*Flows: [02 Donation and box intake](../sequences/02-donation-and-box-intake.puml) ([process](../process/02-donation-and-box-intake.puml)).*

- A **donation** is one donor's drop-off or consignment, containing many items. Items belong to a donation
  ([D14](decisions.md#d14)).
- **Every item is attributed to the donor who gave it,** through its donation ([D9](decisions.md#d9)).
- A donor is **personal data**. The donor is an anonymous key that records and reports reference, and the personal
  details are held separately so they can be erased. Erasing a donor deletes the details and keeps counts and values
  in reports ([D15](decisions.md#d15)).
- After erasure the donor appears as **"Former donor"** on documents already issued ([D28](decisions.md#d28)).
- **Donors have no access to the system.** A donor tells HQ by email that a box is coming, and a Dispatcher or Loader
  may enter it as an expected box ([O22](decisions.md#o22)).
- **A donor status report** shows the items a donor has given and their status, at a high level only. It is produced
  for the donor by a user ([O6](decisions.md#o6)).

## Expiry

- **An item that has already expired blocks its box** ([D1](decisions.md#d1)).
- An item with **short remaining shelf life raises a warning.** The thresholds are one third of the term for food, and
  half of the term or six months for medicines ([D25](decisions.md#d25), unverified).

## Sensitive and prohibited goods

- The **sensitive categories** are medicine, gas canisters, batteries, flammables and food. The list can be
  expanded ([D16](decisions.md#d16)).
- **Sensitive cargo warns the Dispatcher** of the convoy that carries it, citing the box and item, so extra checks
  such as additional insurance can be made. It never blocks ([D7](decisions.md#d7)).
- **Gas canisters, lithium batteries and flammables are not carried.** When one enters inventory the system warns, so
  an appropriate measure can be taken ([D21](decisions.md#d21)).

## Labels and changing a box

*Flows: [02 Donation and box intake](../sequences/02-donation-and-box-intake.puml) ([process](../process/02-donation-and-box-intake.puml)), [03 Box replacement](../sequences/03-box-replacement.puml) ([process](../process/03-box-replacement.puml)).*

- A QR label ties the physical box to its record. It **carries the item list and the name of the person who signed
  what is inside** ([D2](decisions.md#d2)). It never carries a Receiver, region or address.
- **Labels carry both English and Ukrainian** ([O17](decisions.md#o17)). The Ukrainian text is **machine translated
  with no external dependency**, and is not marked as a translation ([O29](decisions.md#o29)).
- **Once a box is attested its contents never change.** If they must, **the box is replaced**: the old box is
  voided, a new box is created and attested with the items copied across, and the old QR label stops resolving
  ([D3](decisions.md#d3)).

## Capacity and allocation

*Flows: [05 Load sign-off and declarations](../sequences/05-load-signoff-and-declarations.puml) ([process](../process/05-load-signoff-and-declarations.puml)), [06 Load change and re-declare](../sequences/06-load-change-and-redeclare.puml) ([process](../process/06-load-change-and-redeclare.puml)).*

- A vehicle records its **cargo space, maximum cargo weight and kerb weight** ([D18](decisions.md#d18)).
- Boxes are allocated to a vehicle by **box volume against the vehicle's cargo volume, never exceeding its cargo weight
  limit.** This is approximate by design. Dispatchers and the Convoy Leader may reallocate boxes between vehicles to
  suit real conditions ([D10](decisions.md#d10)).
- **After a breakdown a box stays with its vehicle** by default. The Convoy Leader may move high-value or sensitive
  boxes by judgement ([D11](decisions.md#d11)).

## Hubs and stock

- **A Loader sees only the locations they manage** ([O14](decisions.md#o14)). **The Administrator assigns** Loaders
  to locations ([O31](decisions.md#o31)).
- Loaders **occasionally review the stock** held in a hub.
- **There is no required order** in which boxes are allocated to convoys ([O14](decisions.md#o14)).
- **Items are not tracked once a convoy is on the road.** A box is allocated to a vehicle and left in it
  ([O15](decisions.md#o15)).

## Receivers and destinations

*Flows: [11 Receiver registration](../sequences/11-receiver-registration.puml) ([process](../process/11-receiver-registration.puml)).*

- A box has a destination **Receiver**, which may be set before the box arrives at a hub.
- **A Receiver has a registration status:** pending, registered, suspended or expired. An Administrator manages it
  ([D22](decisions.md#d22), [D30](decisions.md#d30)).
- **Only a registered Receiver** lets a convoy travel and lets a box be assigned a destination
  ([D35](decisions.md#d35)).
- **A vehicle is handed to a Receiver** ([D23](decisions.md#d23)).
- **What kind of body a Receiver is, and whether a vehicle may lawfully go to them, is not documented or codified** in
  this software ([D33](decisions.md#d33)).
- A **distribution hub** is a Location registered by an Administrator. It can be any site, and has no separate
  status ([D36](decisions.md#d36)).

## Delivery, refusal and loss

*Flows: [08 On the road](../sequences/08-on-the-road.puml) ([process](../process/08-on-the-road.puml)), [09 Delivery, acceptance and closing](../sequences/09-delivery-acceptance-closing.puml) ([process](../process/09-delivery-acceptance-closing.puml)).*

- **A box is delivered when the Convoy Leader or the Dispatcher marks it arrived** ([O8](decisions.md#o8)). It is
  then **accepted** when Ukrainian customs accept it under their acceptance rules. A photo to verify acceptance may be
  added later ([O9](decisions.md#o9)).
- **When customs refuse a box at a border, the Convoy Leader changes its status** to *seized*, or to *returned to a
  hub* ([O2](decisions.md#o2)). The box leaves the vehicle's load, so that vehicle's declarations go stale while they
  are still open ([D13](decisions.md#d13)).
- **Cargo is not insured.** A box that is damaged or stolen is marked *undeliverable*, with the reason
  ([O5](decisions.md#o5)). Insurance is for the vehicle to travel on the road.

## Declarations: the box-side rules

*Flows: [05 Load sign-off and declarations](../sequences/05-load-signoff-and-declarations.puml) ([process](../process/05-load-signoff-and-declarations.puml)), [06 Load change and re-declare](../sequences/06-load-change-and-redeclare.puml) ([process](../process/06-load-change-and-redeclare.puml)).*

The full rules are in [Customs declarations](customs-declarations.md).

- **Moving a box after declarations are filed makes the declarations of both vehicles stale** and gives the
  Dispatcher an immediate re-declare task. The convoy cannot depart until it is resolved
  ([D13](decisions.md#d13), [D27](decisions.md#d27)).
- **If contents change after the Ukrainian goods list is filed,** the goods are held until a new filing is accepted,
  at a registered hub near the border. The loaders and the Dispatcher at that site are responsible for them
  ([D24](decisions.md#d24), [D32](decisions.md#d32)).
- **The Dispatcher prepares a new goods list, and the Receiver re-files it** ([D31](decisions.md#d31)).
- **The Dispatcher is warned one week before** a goods-list code or a customs declaration expires. The validity
  lengths are configuration ([D34](decisions.md#d34), [D37](decisions.md#d37)).

## Reporting

The value report summarises the **count and value of items**, and the **value of vehicles**, for three audiences
([D29](decisions.md#d29)). A vehicle's value is **what was paid for it** ([O11](decisions.md#o11)), or, for a vehicle that was given, a GBP
estimate with its source ([O33](decisions.md#o33)). Vehicle equipment
the charity buys is not part of the value delivered ([O13](decisions.md#o13)).

| Audience | Level of detail |
|---|---|
| HMRC and auditors | Per donation and item, with the value's source |
| Charity Commission | Per financial year, totals of item count and value |
| The public | Per convoy, by category only |

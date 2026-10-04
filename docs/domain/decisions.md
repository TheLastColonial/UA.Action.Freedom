# Decisions

The single record of every decision taken about the domain, and why. The business rules that follow from them are in
the domain documents, which link back here.

| Domain document | Covers |
|---|---|
| [Boxes and donations](boxes-and-donations.md) | Boxes, items, donations, donors, Receivers as destinations, reporting |
| [Convoy operations](convoy-operations.md) | Convoy, vehicle, crew, accommodation, fuel, manifest, the Convoy Leader, readiness |
| [Customs declarations](customs-declarations.md) | GMR, ENS, ELO and the Ukrainian goods list |
| [Key concepts](key-concepts.md) | The shared vocabulary |
| [Ukrainian customs research](ua-customs-requirements.md) | External rules behind some decisions. Not authoritative. |

## How to read this

- Each decision has a stable **ID**. IDs are never reused. Documents link to them as `decisions.md#d3`.
- **D** decisions began in box discussions, **P** in convoy discussions, **X** in declaration discussions and **O**
  in operations discussions (what happens on the road, when things go wrong, and afterwards). The letter says where
  a decision began, not what it governs. The sections below group them by topic.
- A **superseded** decision is struck through and says what replaced it. It is kept so the reasoning is not lost.
- **UNVERIFIED** means the decision rests on a claim that has not been confirmed against a primary source.
- The flows these decisions shape are drawn in the [sequence diagrams](../sequences/README.md) and [process diagrams](../process/README.md), with [state diagrams](../states/README.md), [use cases](../use-cases/README.md), the [domain model](../model/domain-model.puml) and a worked [convoy timeline](../timeline/convoy-timeline.puml).

## Contents

1. [Items, categories and value](#items-categories-and-value)
2. [Expiry and sensitive goods](#expiry-and-sensitive-goods)
3. [Donations and donors](#donations-and-donors)
4. [Labels and changing a box](#labels-and-changing-a-box)
5. [Capacity and allocation](#capacity-and-allocation)
6. [Receivers and destinations](#receivers-and-destinations)
7. [Reporting](#reporting)
8. [Convoy structure and crew](#convoy-structure-and-crew)
9. [Readiness and warnings](#readiness-and-warnings)
10. [The Convoy Leader](#the-convoy-leader)
11. [Declarations and filing](#declarations-and-filing)
12. [Box outcomes and delivery](#box-outcomes-and-delivery)
13. [Insurance, budget and costs](#insurance-budget-and-costs)
14. [Hubs, stock and classification](#hubs-stock-and-classification)
15. [On the road and records](#on-the-road-and-records)
16. [Identity and access](#identity-and-access)
17. [Background and alternatives](#background-and-alternatives)
18. [Consequences and amendments due](#consequences-and-amendments-due)
19. [Architecture decision records](#architecture-decision-records)
20. [Open questions](#open-questions)

---

## Items, categories and value

| ID | Decision | Notes |
|---|---|---|
| <a id="d5"></a>**D5** | **Item value is an estimate in GBP only, with its source recorded** (`Donor` stated, or `Estimate`). | No FX conversion. A USD or PLN value is converted by the person entering it. |
| <a id="d6"></a>**D6** | **Item category is a fixed list plus free text for a new category.** | A fixed category carries hazard class, expiry threshold and commodity code. A free-text category carries none until someone promotes it into the list. |

## Expiry and sensitive goods

| ID | Decision | Notes |
|---|---|---|
| <a id="d1"></a>**D1** | **Already-expired items block a box. Short remaining shelf life warns.** | Thresholds are set by [D25](#d25). |
| <a id="d25"></a>**D25** | **Shelf-life warnings follow the legal minimum:** one third of the term for food, and half or six months for medicines. | **UNVERIFIED.** Found in one secondary source. Confirm against the State Customs Service notice. See [research](ua-customs-requirements.md#4-restricted-or-controlled-goods). |
| <a id="d16"></a>**D16** | **Sensitive categories: medicine, gas canisters, batteries, flammables, food.** The list is expandable. | Refined by [D21](#d21). |
| <a id="d7"></a>**D7** | **Sensitive cargo warns the Dispatcher of the convoy that carries it, citing the box and item.** It never blocks. | Prompts extra checks, such as additional insurance. |
| <a id="d21"></a>**D21** | **Gas canisters, lithium batteries and flammables are not carried.** The system warns when one enters inventory so an appropriate measure can be taken. | The warning fires when the item is added, not when a convoy is built. |

## Donations and donors

| ID | Decision | Notes |
|---|---|---|
| <a id="d14"></a>**D14** | **A `Donation` is its own entity:** one donor, one drop-off or consignment, many items. | Enables donor receipts and reporting. |
| <a id="d9"></a>**D9** | **Every item is attributed to the donor who gave it,** through its donation. | |
| <a id="d15"></a>**D15** | **Donors follow the volunteer privacy pattern:** an anonymous key that records and reports reference, with personal details in a separate, erasable record. | Erasure deletes the details and keeps counts and values in reports. |
| <a id="d28"></a>**D28** | **An erased donor appears as "Former donor"** on documents already issued. | Same as volunteers. |

## Labels and changing a box

| ID | Decision | Notes |
|---|---|---|
| <a id="d2"></a>**D2** | **The QR label carries the item list and the validator's name.** | Reverses the earlier rule, in [Key concepts](key-concepts.md#box), that a label carries only a box number, token and charity name. Receiver, region and address stay off it. **Needs a data-sensitivity review.** **Amended 2026-10-04 (plan 16 sign-off):** one line per item, showing its category and quantity (and its expiry date where it has one), in English and Ukrainian; **never** properties, value, donor, donation or free text. The signer is shown by **both a signer code and first name with last initial**; an erased volunteer reads "Former volunteer" with the code kept. Medicine appears as its category. See the [label review](../security/0011-label-review.md). |
| <a id="d3"></a>**D3** | **A validated box is never edited. If its contents must change, the box is replaced.** | The old box is voided, a new box is created and attested, items are copied across, and the old QR stops resolving. Replaces the idea of unlock and re-validate. |

## Capacity and allocation

| ID | Decision | Notes |
|---|---|---|
| <a id="d18"></a>**D18** | **A vehicle records cargo space, maximum cargo weight and kerb weight.** | Inputs to [D10](#d10). Cargo capacity was optional before. |
| <a id="d10"></a>**D10** | **Capacity is box volume against vehicle cargo volume, never exceeding the vehicle's cargo weight limit.** | Deliberately approximate. Dispatchers and the Convoy Leader may reallocate boxes between vehicles to suit real conditions. |
| <a id="d11"></a>**D11** | **After a breakdown a box stays with its vehicle by default.** The Convoy Leader may move high-value or sensitive boxes by judgement. | Repacking at the Convoy Leader's discretion is a future capability. |

## Receivers and destinations

| ID | Decision | Notes |
|---|---|---|
| <a id="d22"></a>**D22** | **A Receiver has a registration status.** | Replaces a bare receiver with a registered-recipient check. |
| <a id="d30"></a>**D30** | **Statuses are pending, registered, suspended and expired,** managed by an Administrator. | Administrator-owned, like volunteer approval. |
| <a id="d35"></a>**D35** | **Only a `registered` Receiver lets a convoy travel and lets a box be assigned a destination.** Pending, suspended and expired block both. | Which transitions are allowed is left to implementation, with the Administrator as the only actor. |
| <a id="d23"></a>**D23** | **Vehicles are handed to a Receiver.** | See [P5](#p5) and [P11](#p11). |
| <a id="d33"></a>**D33** | **What kind of body a Receiver is, and whether a vehicle may lawfully go to them, is out of scope and is not documented or codified.** | Receivers are the most sensitive data in the system. No field, status, document or test describes it. |
| <a id="d36"></a>**D36** | **A distribution hub is a `Location` registered by an Administrator.** It can be any site, and there is no separate hub status. | Real places are held as data, never in code or documents. |

## Reporting

| ID | Decision | Notes |
|---|---|---|
| <a id="d29"></a>**D29** | **Three audiences, three levels of detail.** HMRC and auditors: per donation and item, with value source. Charity Commission: per financial year, totals of item count and value. Public: per convoy, by category only. | Confirmed as proposed. |
| <a id="d19"></a>~~**D19**~~ | ~~One value report for three audiences.~~ | **Superseded by [D29](#d29).** |

## Convoy structure and crew

| ID | Decision | Notes |
|---|---|---|
| <a id="p1"></a>**P1** | **Ferry bookings are per vehicle and outbound only,** recording a reference number and ticket details. | Vehicles are donated and do not come back. |
| <a id="p2"></a>**P2** | **Accommodation is booked per crew member** and linked to a **route point**. | |
| <a id="p3"></a>**P3** | **The convoy has a fuel budget** set before departure. The **Convoy Leader** records fuel spent on the road. | The budget is widened beyond fuel by [O12](#o12). Recording fuel stays with the Convoy Leader. |
| <a id="p5"></a>**P5** | **Boxes have their own Receivers. A vehicle has its own handover Receiver,** which may differ. Delivery is recorded per box and per vehicle. | |
| <a id="p6"></a>**P6** | **`Manifest` keeps its name.** Its definition changes to the load sign-off and the document pack. | |
| <a id="p8"></a>**P8** | **Accommodation must cover every crew member at every overnight stop.** A booking may cover several people who share. | Drivers never sleep in vehicles or at home. [O4](#o4) lets a crew member who arranges their own accommodation satisfy the requirement. |
| <a id="p10"></a>**P10** | **Driver documents are out of scope.** The Dispatcher verifies driving rights before assigning a driver. The system stores no licence, passport or permit data. | |
| <a id="p12"></a>**P12** | **Journey legs are removed from the model for now,** to be reintroduced later if needed. A crew seat is one person on one vehicle on one convoy. | Reverses part of [ADR 0001](../adr/0001-truck-list-as-a-table.md). A crew handover at the border can no longer be recorded. See [Consequences](#consequences-and-amendments-due). |
| <a id="p13"></a>**P13** | **If a crew member leaves, their accommodation stays booked** and the Dispatcher may try to cancel or refund it. **If they are replaced, the booking can be migrated** to the new driver. | The [P8](#p8) requirement is unmet for a replacement until the booking is migrated or a new one made. |
| <a id="p15"></a>**P15** | **A route point is an overnight stop only if the Dispatcher flags it.** The service never calculates routes or times to decide this. | |
| <a id="p16"></a>**P16** | **A booking left by a departed crew member is a warning and a Dispatcher task** ("cancel or migrate"). It does not block departure. | |

## Readiness and warnings

| ID | Decision | Notes |
|---|---|---|
| <a id="p4"></a>**P4** | **Blocking requirements for departure:** declarations current; at least one driver per vehicle; insurance for each vehicle; ferry booked for each vehicle; accommodation booked; Convoy Leader assigned; every box validated and with a registered Receiver. | Extended by [D35](#d35), [P9](#p9) and [P11](#p11). The full list is in [Convoy operations](convoy-operations.md#readiness). |
| <a id="p9"></a>**P9** | **One driver per vehicle blocks departure. Two drivers per vehicle is advisory.** | Was "two drivers per leg" until [P12](#p12). |
| <a id="p11"></a>**P11** | **A vehicle's handover Receiver must be set, and registered, before departure,** exactly as for a box. | |
| <a id="p17"></a>**P17** | **Advisory warnings adopted:** journey timing, data quality, and Receivers and fuel. **Load-efficiency warnings are not adopted.** | None of them block. Not adopted: boxes allocated but not loaded, validated boxes with nothing allocated, a vehicle carrying much less than it could. |
| <a id="o36"></a>**O36** | **A convoy departs by one action on the convoy, taken by the Dispatcher,** and it is refused unless every blocking requirement holds for the convoy and for each vehicle still travelling. | Replaces the manifest's per-vehicle `depart` transition. |

## The Convoy Leader

| ID | Decision | Notes |
|---|---|---|
| <a id="d8"></a>**D8** | **The Convoy Leader is a Driver on the convoy,** responsible for it and the contact point to HQ. | One per convoy. |
| <a id="p7"></a>**P7** | **"Lead driver" and "Convoy Leader" are the same role.** The term is Convoy Leader, and there is exactly one per convoy. | No lead driver exists per vehicle, so the decision in [ADR 0001](../adr/0001-truck-list-as-a-table.md) to drop the primary driver stands. |
| <a id="d17"></a>**D17** | **The Dispatcher nominates the Convoy Leader** when creating or managing the convoy. Any Driver may be nominated, and it may change during the convoy's life. | The history of who held the role is kept. |
| <a id="p14"></a>**P14** | **Only a Dispatcher or an Administrator may reassign the Convoy Leader.** Fuel entries stay with the leader who entered them. | |
| <a id="d26"></a>**D26** | **The Convoy Leader may move boxes and confirm delivery.** | The earlier clause "but never sees sensitive addresses" is superseded by [X7](#x7). |
| <a id="x2"></a>**X2** | **The Convoy Leader marks each border as crossed** from a **checklist page in the convoy section**, which lists the route's points in order. Fuel is entered on the same page. | A crossing is an event on a route point. |
| <a id="x7"></a>**X7** | **The Convoy Leader sees every address they need to drive to, including the final destination,** so the convoy can arrive together. | **Widens the receiver segregation.** Safeguards in [X12](#x12). |
| <a id="x8"></a>~~**X8**~~ | ~~Access begins on nomination.~~ | **Superseded by [X11](#x11).** |
| <a id="x9"></a>**X9** | **The Convoy Leader sees all route stops and the final destination of their own convoy.** ~~They do not see every Receiver's address for every vehicle.~~ | The struck clause is **superseded by [O26](#o26)**. |
| <a id="x10"></a>**X10** | **The checklist is a web page used on a phone over the internet.** No app is installed, and nothing is stored on the device. | |
| <a id="x11"></a>**X11** | **Address access opens 14 days before the planned departure** and ends on reassignment or on arrival. | The figure is configuration. |
| <a id="x12"></a>**X12** | **Four safeguards on address access are approved:** scoped to their own convoy; a new, narrower permission, not an extension of `receivers:detail`; every read audited and made through the sensitive path; never printed or logged. | Three controls already protect an address: the `receivers:detail` policy, a separate database identity, and a database `DENY`. A Convoy Leader is a fourth reader. |
| <a id="x13"></a>**X13** | **Before the address window opens ([X11](#x11)), the Convoy Leader sees route point headers** (name and kind, such as "UK port" or "overnight stop") **but not their details.** | Details appear when the window opens, just before departure. |
| <a id="o26"></a>**O26** | **The Convoy Leader sees the route and the addresses of all Receivers on their convoy** (every box's and every vehicle's Receiver), within the window set by [X11](#x11). | Resolves [Q-final-destination](#q-final-destination). Supersedes the limit in [X9](#x9). The [X12](#x12) safeguards apply to every read. |
| <a id="o27"></a>**O27** | **A Dispatcher may record route marks and border crossings on the Convoy Leader's behalf,** from a radio or phone report, and is named as the person who recorded them. **Only the time of entry is recorded,** not a separate time it happened. | Resolves [Q-crossing-fallback](#q-crossing-fallback) and [Q-occurrence-time](#q-occurrence-time). |

## Declarations and filing

| ID | Decision | Notes |
|---|---|---|
| <a id="x6"></a>**X6** | **Every declaration is per vehicle,** because that is what is declared at the border. The Ukrainian goods list is **one per Receiver, per vehicle, per convoy**. | Same granularity as the GMR, ENS and ELO. Replaced an earlier "one per convoy" decision. |
| <a id="d4"></a>**D4** | **UK and French declarations are recorded and generated now, and submitted electronically later.** | Revised after research found no API route into Ukraine's system. Submission mode is configurable ([X5](#x5)). |
| <a id="x5"></a>**X5** | **Filing is manual by default.** The APIs will be built, but the system must not assume they are operating. Each authority has a configurable submission mode. | Manual filing means a Dispatcher records the authority's reference. Today, approving a manifest enqueues the GMR and ELO automatically, and that becomes opt-in. |
| <a id="d12"></a>**D12** | **The Dispatcher files the UK and French declarations, including the ENS ([X1](#x1)), and prepares the Ukrainian goods list.** The Receiver files the Ukrainian one. | Matches `manifests:declare` today. |
| <a id="d20"></a>**D20** | **Ukraine's goods list is filed by the Receiver, not by us.** The system prepares it, rolled up from boxes, and tracks the filing's reference, status and who recorded it. | Per research, a Ukrainian recipient files in Ukraine's system. See [research](ua-customs-requirements.md#2-documents-and-declarations-for-a-road-convoy). |
| <a id="x1"></a>**X1** | **The Dispatcher files the ENS.** A Ground Officer may work alongside to answer questions about destinations. | Segregation is unchanged: how the Ground Officer answers is outside the system, and the system never shows the address to a Dispatcher ([ADR 0003](../adr/0003-ens-declaration-recorded-not-submitted.md)). |
| <a id="d13"></a>**D13** | **Moving a box after declarations are filed makes them stale and gives the Dispatcher an immediate re-declare task.** The convoy cannot depart until it is resolved. | |
| <a id="d27"></a>**D27** | **A box move makes the declarations of both affected vehicles stale,** including the Ukrainian goods lists, since those are per vehicle ([X6](#x6)). | |
| <a id="x3"></a>**X3** | **Every load change after sign-off needs Administrator re-approval.** Ukrainian documents are refiled. UK and French declarations are completed by hand in their portals for now. | A load change has significant impact. Reverses the earlier rule, in [Manifest Status](key-concepts.md#manifest-status), that nothing may reopen a confirmed manifest. |
| <a id="d24"></a>**D24** | **If contents change after the Ukrainian goods list is filed, the goods are held until a new filing is accepted,** at a registered hub near the border ([D32](#d32)). | The convoy's status reflects "held, awaiting re-filing". |
| <a id="d31"></a>**D31** | **When contents change, the Dispatcher prepares a new goods list and the Receiver re-files it.** | |
| <a id="d32"></a>**D32** | **Goods held for re-filing go to another registered distribution hub, near the border.** The loaders and the Dispatcher at that site are responsible for them. | Uses the existing `Location` model ([D36](#d36)). |
| <a id="d34"></a>**D34** | **The Dispatcher is warned one week before a goods-list code or a customs declaration expires.** | Validity lengths are UNVERIFIED. |
| <a id="d37"></a>**D37** | **Deadline warnings are added now.** Validity lengths are configuration and may change later. | They must not be hard-coded. |
| <a id="o32"></a>**O32** | **The consignee's address on an ENS is entered by the Ground Officer** in the EU portal, alongside the Dispatcher who files it. Freedom keeps withholding the address from the Dispatcher. | Resolves [Q-ens-address-handling](#q-ens-address-handling). A process rule outside the system. |

## Box outcomes and delivery

| ID | Decision | Notes |
|---|---|---|
| <a id="o1"></a>**O1** | **Only the Convoy Leader is given route and destination details.** Other drivers are not. The Convoy Leader communicates with every member of the convoy by radio. | Replaces the earlier principle in [Data Sensitivity](key-concepts.md#data-sensitivity) that precise delivery detail "is released to the driver at the point of delivery". Builds on [X7](#x7). |
| <a id="o2"></a>**O2** | **When customs refuse a box at a border, the Convoy Leader changes its status** to **seized** (customs keep it) or **returned to a hub**. | Extends [D26](#d26). The box leaves the vehicle's load, so the declarations for that vehicle go stale ([D13](#d13)) while they are still open. |
| <a id="o28"></a>**O28** | **A box returned to a hub goes to the registered hub the Convoy Leader chooses at the time.** It becomes an arrived box there again, **with its label and contents unchanged**, and may be allocated to a later convoy. | Resolves [Q-returned-box](#q-returned-box). |
| <a id="o5"></a>**O5** | **Cargo is not insured.** A box that is damaged or stolen is marked **undeliverable**, with the reason. Insurance is for the vehicle to travel on the road. | Replaces the earlier "lost" outcome for a box. |
| <a id="o8"></a>**O8** | **A box or vehicle is delivered when the Convoy Leader or the Dispatcher marks it arrived.** | |
| <a id="o9"></a>**O9** | **There is an additional status, "accepted",** recording that Ukrainian customs have accepted the delivery under their acceptance rules. A photo upload to verify acceptance may be added later. | Follows delivered. Granularity set by [O24](#o24). |
| <a id="o24"></a>**O24** | **Acceptance is recorded once per Ukrainian goods list,** by the Convoy Leader or the Dispatcher. Every box on that list becomes accepted. | Resolves [Q-accepted-granularity](#q-accepted-granularity). |
| <a id="o10"></a>**O10** | **A convoy is closed once it has arrived, and closing generates a report** of expected versus actual costs, and of the boxes delivered or not. | Reimbursing volunteers is not managed by the system for now. Who closes, and whether it locks: [O25](#o25). |
| <a id="o25"></a>**O25** | **The Dispatcher closes a convoy, and closing does not lock it.** Later corrections are allowed, and the report can be regenerated. | Resolves [Q-close-convoy](#q-close-convoy). |
| <a id="o6"></a>**O6** | **A donor status report shows the items a donor has given and their status, at a high level only.** Donors have no access to the system. | The report is produced for the donor by a user. |
| <a id="o22"></a>**O22** | **A donor tells HQ by email that a box is coming, and a Dispatcher or Loader may enter it as an expected box.** Entering it early is optional. It can be entered when it arrives. A thank-you when items reach their destination may be explored later. | The focus is boxes and vehicles reaching their destinations. |

## Insurance, budget and costs

| ID | Decision | Notes |
|---|---|---|
| <a id="o7"></a>**O7** | **Removing a crew member does not void a vehicle's insurance.** The other members stay covered. **Adding a driver needs the insurance updated** with the insurer, which the Dispatcher records. There is no cut-off after which a change needs extra approval. | Replaces the earlier rule, in [Vehicle Insurance](key-concepts.md#vehicle-insurance), that any crew change voids it. |
| <a id="o3"></a>**O3** | **Blocking requirements are not overridden.** They are business rules, and, for example, a vehicle is never driven without insurance. | Where a real need exists the rule is changed, as [O4](#o4) does for accommodation. |
| <a id="o4"></a>**O4** | **A crew member can be flagged as arranging their own accommodation** at an overnight stop, which satisfies the accommodation requirement for that person ([P8](#p8)). | For example, staying with family. Granularity set by [O30](#o30). |
| <a id="o30"></a>**O30** | **Self-accommodation is flagged per crew member, per overnight stop.** | Resolves [Q-self-accommodation](#q-self-accommodation). |
| <a id="o12"></a>**O12** | **A convoy has a budget with a line for each cost type** (fuel, ferry, hotel, insurance and others), and records **actual costs** against each. Allocating the budget is a **step in creating a convoy**. An approval flow may follow later. | Widens [P3](#p3), which covered fuel only. |
| <a id="o37"></a>**O37** | **A budget is not required for a convoy to depart.** An unset budget is an advisory warning. | Resolves [Q-budget-required](#q-budget-required). Consistent with [P17](#p17). |
| <a id="o11"></a>**O11** | **A vehicle's value is what was paid for it,** and the value report includes vehicles. | A vehicle that was given: [O33](#o33). |
| <a id="o33"></a>**O33** | **A vehicle that was given, not bought, is valued at a GBP estimate, with its source recorded,** as for an item ([D5](#d5)). | Resolves [Q-vehicle-without-price](#q-vehicle-without-price). |
| <a id="o13"></a>**O13** | **Vehicle equipment bought by the charity** (warning triangles and the like) **is accounted for separately** from donations. It is added to the vehicles in **a step of creating a convoy**, has no donor, and is **not part of the value delivered**. | Scope is open: [Q-vehicle-equipment](#q-vehicle-equipment). |

## Hubs, stock and classification

| ID | Decision | Notes |
|---|---|---|
| <a id="o14"></a>**O14** | **A Loader sees only the locations they manage.** Loaders occasionally review the stock in a hub. **There is no required order** in which boxes are allocated to convoys. | Narrows what a Loader can see. Who assigns: [O31](#o31). |
| <a id="o31"></a>**O31** | **The Administrator assigns Loaders to the locations they manage, and maintains the mapping from categories to declaration codes.** | Resolves [Q-admin-assignments](#q-admin-assignments). Both are reference data, as locations and bays are. |
| <a id="o15"></a>**O15** | **Items are not tracked once a convoy is on the road.** A box is allocated to a vehicle and left in it. **Fuel is not carried.** | Consistent with [D21](#d21). |
| <a id="o16"></a>**O16** | **A Loader classifies each item by category,** and each category maps to the code used in each authority's declaration. | Mapping, not data entry, so a category is classified once ([D6](#d6)). A free-text category has no mapping until it is added. |
| <a id="o17"></a>**O17** | **Labels carry both English and Ukrainian.** | Source of the Ukrainian text: [O29](#o29). |
| <a id="o29"></a>**O29** | **The Ukrainian text on a label is machine translated, with no external dependency,** and is **not marked** as a machine translation. The option is chosen by a time-boxed spike: a Microsoft offline option first, otherwise an open-source model run in-process. | Resolves [Q-label-ukrainian-text](#q-label-ukrainian-text). The spike, and a data-sensitivity review of the label, gate [ADR 0011](../adr/0011-attested-boxes-are-replaced-not-edited.md). **Amended 2026-10-04:** the spike found no Microsoft built-in offline translation, and nothing is built or chosen yet ([Q-label-translation](#q-label-translation)). Until it is solved the Ukrainian on a label is the category's `NameUk`, which the Administrator edits; machine translation is a seam that is not wired, and any later machine suggestion is **confirmed by the attesting Loader** before it prints. See the [spike](../spikes/0011-offline-translation.md). |

## On the road and records

| ID | Decision | Notes |
|---|---|---|
| <a id="o18"></a>**O18** | **If the page is unavailable, the fallback is to call HQ and use printed documents.** | See [X10](#x10). |
| <a id="o19"></a>**O19** | **Every entity records who last changed it,** and when. | Built by [plan 03](../plans/03-last-changed-audit.md): `LastChangedBy`/`LastChangedAt` on every entity table, stamped in the same statement as the change. |
| <a id="o20"></a>**O20** | **There is no live tracking and no GPS,** because of connectivity and security concerns. **The Convoy Leader marks arrival at each route point and each accommodation,** and HQ sees progress from those marks. | |
| <a id="o21"></a>**O21** | **Notifications are shown on screen.** Email notifications may be built later. | |
| <a id="o23"></a>**O23** | **Design assumptions:** about 500 vehicles over four years of operation, **one convoy a month**, and **about 25 users**. | Not a rule. A guide for sizing and for what is worth building. |

## Identity and access

| ID | Decision | Notes |
|---|---|---|
| <a id="o34"></a>**O34** | **A login is linked to a person by storing the identity provider's subject on the person's erasable details,** linked by an Administrator. Erasing the person removes the link with the rest of their personal data. | Needed by [O19](#o19), [X12](#x12) and [O14](#o14). Under review: [Q-staff-and-volunteer-identity](#q-staff-and-volunteer-identity). Built by [plan 02](../plans/02-login-person-link.md): `PersonDetail.IdentitySubject`, `PUT /people/{id}/login`, `GET /me`. |
| <a id="o35"></a>**O35** | **A login that is not linked to a person is refused (403) on any write that records who did it.** No "unknown" identity is ever written. | Built by [plan 02](../plans/02-login-person-link.md): `ICurrentPerson`, and the 403 `login-not-linked` problem. It used to record "unknown". |

---

## Background and alternatives

### Why the convoy model changed

`Manifest` had become the place where unrelated things collect. It was at once the **cargo list**, the **customs
paperwork for three authorities**, the **ferry booking**, the **approval sign-off** and the **delivery status**.
These change for different reasons, on different schedules, and are owned by different roles. One ten-state enum
cannot describe them all: "approved, but the French envelope is refused and one box was moved" has no single status.
Readiness was also computed from too narrow a slice of the facts to say what was outstanding.

The structure in [ADR 0001](../adr/0001-truck-list-as-a-table.md) was sound, and the truck-list entry stayed as the
per-vehicle anchor.

### Alternatives considered

| Option | Verdict |
|---|---|
| **Manifest as the central object** (as before) | One thing to look at, but every new kind of fact makes it larger and its status more overloaded. |
| **A `Dispatch Plan` above the convoy** | Adds a layer that duplicates what `Convoy` already is. The convoy is already the unit that is planned. |
| **Enrich `Convoy`, introduce `Declaration`, compute readiness** | **Chosen.** |

## Consequences and amendments due

### Against what exists today

| Area | Before | After |
|---|---|---|
| `Manifest` | cargo, GMR, ELO, ENS, ferry and status | load sign-off only. Documents are generated. |
| Readiness | advisory: route, crew, insurance | requirement checklist, with blocking requirements |
| Delivery | manifest status | per box, rolled up per vehicle |
| Convoy Leader | not modelled | one per convoy, with a scoped checklist page |
| Box outcomes | delivered, lost or returned | delivered, accepted, seized, returned to a hub, or undeliverable ([O2](#o2), [O5](#o5), [O9](#o9)) |
| Convoy end | arrival | arrival, then closing with a report ([O10](#o10)) |
| Vehicle value | no value | price paid, included in the value report ([O11](#o11)) |
| Loader | sees every location | sees only the locations they manage ([O14](#o14)) |
| Item classification | free text and properties | category, mapped to each authority's codes ([O16](#o16)) |
| Label | one language | English and Ukrainian ([O17](#o17)) |

### Other consequences

- **A new capability for a driver:** marking a route point reached and entering fuel, **on one convoy only**. All
  roles so far are global.
- **A new amendment path** for the GMR, ENS and ELO, which today are written once.
- **The rule that nothing may reopen a confirmed manifest is reversed** ([X3](#x3)), and would be recorded in an ADR.

### Documents outside `docs/domain/` still to amend

[ADRs 0001, 0002 and 0003](../adr/README.md) have been amended. The rest are amended in the same change as the code
that needs them.

| Document | Change |
|---|---|
| `CLAUDE.md` | Architecture and domain model: legs, manifest, readiness, Convoy Leader. |
| [Gotchas and open questions](../gotchas-and-open-questions.md) | Crew per leg (§ on `ConvoyVehicleCrew` and the per-leg unique key, and the commitment note), the manifest freeze. |
| [Local authentication](../local-authentication.md) | The line saying crewing "is per journey leg". Loader scope ([O14](#o14)) and the Convoy Leader permission ([X12](#x12)). |
| `CLAUDE.md` and the code | The rule that a crew change voids insurance ([O7](#o7)). |

The domain documents describe the **business rules**. Where code or the documents above still describe legs or the
earlier manifest, they are behind and are to be brought into line.

### Sections of Key concepts still to align

Journey legs, the readiness rules, insurance, the Loader's scope, notifications, the scale note and the rule that only
the Convoy Leader sees destinations have been brought into line. These sections still describe the earlier model:

| Section | Change |
|---|---|
| [Roles](key-concepts.md#roles) | Add the Convoy Leader. Add what a Dispatcher and Administrator do with Receiver status. |
| [Vehicle](key-concepts.md#vehicle) | Cargo capacity is required, not optional ([D18](#d18)). |
| [Item](key-concepts.md#item) and [Box](key-concepts.md#box) | Donor, replace-not-edit, label contents and languages, delivery and refusal outcomes ([D2](#d2), [D3](#d3), [D14](#d14), [O2](#o2), [O5](#o5), [O9](#o9), [O17](#o17)). Value and source, category and its code mapping and expiry are in ([D5](#d5), [D6](#d6), [O16](#o16)). |
| [Arrival](key-concepts.md#arrival) | Closing a convoy and its report ([O10](#o10)). |
| [Receiver](key-concepts.md#receiver) | Registration status ([D30](#d30), [D35](#d35)). |
| [Manifest](key-concepts.md#manifest) and [Manifest Status](key-concepts.md#manifest-status) | Redefined as the load sign-off, with re-approval on change ([P6](#p6), [X3](#x3)). |
| [Documents](key-concepts.md#documents) | Declarations become one concept, with a submission mode ([X5](#x5)). |
| [Data Sensitivity](key-concepts.md#data-sensitivity) | The Convoy Leader's scoped access to addresses and its safeguards ([X7](#x7) to [X13](#x13)). |

## Architecture decision records

The decisions above are recorded, with their context and alternatives, in the ADRs. The [ADR index](../adr/README.md)
lists them all.

| ADR | Records |
|---|---|
| [0004 The manifest is the load sign-off](../adr/0004-the-manifest-is-the-load-sign-off.md) | [P6](#p6), [X3](#x3), [P1](#p1), [P5](#p5) |
| [0005 Declarations are per vehicle, with derived staleness](../adr/0005-declarations-are-per-vehicle-with-derived-staleness.md) | [X6](#x6), [D13](#d13), [D27](#d27), [D24](#d24), [D31](#d31), [X2](#x2) |
| [0006 Filing is manual by default](../adr/0006-filing-is-manual-by-default.md) | [X5](#x5), [D4](#d4), [D12](#d12), [D20](#d20), [X1](#x1), [O32](#o32) |
| [0007 Journey legs are removed from the crew model](../adr/0007-journey-legs-are-removed-from-the-crew-model.md) | [P12](#p12), [P9](#p9) |
| [0008 Readiness is computed, and blocking rules are not overridden](../adr/0008-readiness-is-computed-and-blocking-rules-are-not-overridden.md) | [P4](#p4), [P9](#p9), [P11](#p11), [P17](#p17), [O3](#o3), [O4](#o4), [O7](#o7), [O30](#o30), [O36](#o36), [O37](#o37) |
| [0009 The Convoy Leader reads destination addresses](../adr/0009-convoy-leader-reads-destination-addresses.md) | [X7](#x7) to [X13](#x13), [O1](#o1), [O26](#o26) |
| [0010 Resource-scoped permissions](../adr/0010-resource-scoped-permissions.md) *(proposed)* | [X12](#x12), [O14](#o14), [D17](#d17), [P14](#p14), [O31](#o31), [O34](#o34), [O35](#o35) |
| [0011 An attested box is replaced, never edited](../adr/0011-attested-boxes-are-replaced-not-edited.md) | [D2](#d2), [D3](#d3), [O17](#o17), [O29](#o29) |
| [0012 Receiver registration gates convoys and boxes](../adr/0012-receiver-registration-gates-convoys-and-boxes.md) | [D22](#d22), [D30](#d30), [D33](#d33), [D35](#d35), [D36](#d36) |
| [0013 Donors are a split identity](../adr/0013-donors-are-a-split-identity.md) | [D9](#d9), [D14](#d14), [D15](#d15), [D28](#d28), [O6](#o6), [O22](#o22) |
| [0014 Items are classified by category and valued in GBP](../adr/0014-items-are-classified-by-category-and-valued-in-gbp.md) | [D5](#d5), [D6](#d6), [O11](#o11), [O13](#o13), [O16](#o16), [O31](#o31), [O33](#o33) |
| [0015 Box and vehicle outcomes, and convoy closing](../adr/0015-box-and-vehicle-outcomes-and-convoy-closing.md) | [O2](#o2), [O5](#o5), [O8](#o8), [O9](#o9), [O10](#o10), [O24](#o24), [O25](#o25), [O28](#o28) |
| [0016 Progress is reported, not tracked](../adr/0016-progress-is-reported-not-tracked.md) | [O18](#o18), [O20](#o20), [X10](#x10), [O27](#o27) |
| [0017 Every entity records its last change](../adr/0017-every-entity-records-its-last-change.md) | [O19](#o19), [O34](#o34), [O35](#o35) |

**Decisions with no ADR of their own**, because they are features and not architecture: the budget and vehicle
equipment steps ([O12](#o12), [O13](#o13)), notifications on screen ([O21](#o21)), the expiry and sensitive-goods
rules ([D1](#d1), [D7](#d7), [D16](#d16), [D21](#d21), [D25](#d25)), reporting ([D29](#d29)), capacity
([D10](#d10), [D11](#d11), [D18](#d18)), and the scale assumptions ([O23](#o23)).

## Open questions

| ID | Question | Who can answer |
|---|---|---|
| <a id="q-multi-receiver-border"></a>**Q-multi-receiver-border** | A vehicle carrying boxes for several Receivers has several goods lists ([X6](#x6)). Does Ukrainian customs accept that as one declaration naming several Receivers, or several declarations? Until it is known, **we do not assume the border will accept it.** | A Receiver or a customs broker |
| <a id="q-deadline-lengths"></a>**Q-deadline-lengths** | The 90-day goods-list code and 30-day declaration validity periods are unverified ([D34](#d34)). | A Receiver or a customs broker |
| <a id="q-ua-research-gaps"></a>**Q-ua-research-gaps** | Verify the 2022 UK and EU easements, the medicine shelf-life waiver ([D25](#d25)), and the Slovak, Romanian and Moldovan routes. | A customs broker |

None of these changes the model. The cautious answer is assumed for each.

### For the project owner

| ID | Question | Assumed meanwhile |
|---|---|---|
| <a id="q-retention"></a>**Q-retention** | How long must declarations, audit trails and donor records be kept? Customs generally require years, but donor erasure ([D15](#d15)) pulls the other way. Which wins for each kind of record? | Nothing is deleted except by an erasure request. |
| <a id="q-vehicle-equipment"></a>**Q-vehicle-equipment** | [O13](#o13) covers equipment the charity buys, such as warning triangles. What else is carried for the convoy itself: spare parts, tools, tow straps, drivers' bags? How should those be captured, and do they count towards the vehicle's weight? | Not captured. The fixed weight allowance in [Key concepts](key-concepts.md#manifest) stands. |
| <a id="q-label-translation"></a>**Q-label-translation** | There is **no Microsoft built-in offline English to Ukrainian translation** usable from .NET in a Linux container at no fixed cost (the disconnected Translator container needs strategic-customer approval and an annual commitment). An open-source model (OPUS-MT) works offline but about half of short item names came out wrong, and it needs about 1 GiB. How should free-text Ukrainian be produced, and where should the translator run (API image or a worker)? Better options are to be investigated. [Spike](../spikes/0011-offline-translation.md). | The project owner |
| <a id="q-label-translator-licence"></a>**Q-label-translator-licence** | If an open-source translation model is shipped, which licence notice applies? The OPUS-MT `en-uk` model card says Apache 2.0, a community conversion says CC BY 4.0. Not decided and not built. | The project owner |
| <a id="q-label-signer-erasure"></a>**Q-label-signer-erasure** | The label prints the signer's first name and last initial as well as a signer code ([D2](#d2)). A printed name cannot be erased from a box that has shipped. Accepted by the owner on 2026-10-04; revisit with the retention question ([Q-retention](#q-retention)). | The project owner |
| <a id="q-label-renderer-type"></a>**Q-label-renderer-type** | The label renderer takes a purpose-built `LabelLine` type, never a box or item read model. Accepted by the owner as "not sure but yes"; flagged for revisit. | The project owner |
| <a id="q-staff-and-volunteer-identity"></a>**Q-staff-and-volunteer-identity** | Is a login linked to a person ([O34](#o34)) the right way to manage who is on the system? Volunteers may never have a login, while charity employees have logins and create convoys, manage boxes and handle destinations. Four doubts: (1) an employee has to be a volunteer record, with a date of birth, joined date and driving flag; (2) the Administrator links by pasting a raw token subject, which Keycloak regenerates when the realm is recreated; (3) erasing a person also erases their login link, which suits a volunteer but not a leaver who must keep their attributed history; (4) what a person is and what they may do live in two places, the person record and the identity provider roles. Options: keep the structure and link by verified email on first sign-in with Administrator approval; let a person be staff without volunteer-only fields; or later split a separate login/account entity behind `ICurrentPerson`. | The project owner |

### Resolved on 2026-10-02

Kept so that links to them still land. Each is answered by a decision.

| ID | Question | Answered by |
|---|---|---|
| <a id="q-accepted-granularity"></a>**Q-accepted-granularity** | Who marks a delivery "accepted", and for what? | [O24](#o24): per goods list, by the Convoy Leader or Dispatcher |
| <a id="q-returned-box"></a>**Q-returned-box** | Which hub does a returned box go to, and can it be reused? | [O28](#o28): the hub the leader chooses; reusable unchanged |
| <a id="q-close-convoy"></a>**Q-close-convoy** | Who closes a convoy, and does closing lock it? | [O25](#o25): the Dispatcher; no lock |
| <a id="q-label-ukrainian-text"></a>**Q-label-ukrainian-text** | Where does the label's Ukrainian text come from? | [O29](#o29): machine translation, no external dependency, unmarked |
| <a id="q-vehicle-without-price"></a>**Q-vehicle-without-price** | What is a given vehicle worth? | [O33](#o33): a GBP estimate, with its source |
| <a id="q-self-accommodation"></a>**Q-self-accommodation** | Per stop, or per convoy? | [O30](#o30): per crew member, per stop |
| <a id="q-admin-assignments"></a>**Q-admin-assignments** | Who assigns Loaders and maintains the code mapping? | [O31](#o31): the Administrator |
| <a id="q-budget-required"></a>**Q-budget-required** | Must a budget exist before departure? | [O37](#o37): no, a warning |
| <a id="q-ens-address-handling"></a>**Q-ens-address-handling** | How is the ENS consignee address supplied? | [O32](#o32): the Ground Officer enters it in the portal |
| <a id="q-final-destination"></a>**Q-final-destination** | What does "the final destination" mean? | [O26](#o26): the route and all Receivers on the convoy |
| <a id="q-crossing-fallback"></a>**Q-crossing-fallback** | May a Dispatcher record a crossing for the leader? | [O27](#o27): yes, named |
| <a id="q-occurrence-time"></a>**Q-occurrence-time** | Record when it happened, as well as when it was entered? | [O27](#o27): entered time only |

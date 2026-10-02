# Ukrainian customs requirements for humanitarian aid by road

| | |
|---|---|
| **Status** | Research input. **Not authoritative and not legal advice.** |
| **Compiled** | 2026-10-01, from web sources only. |
| **Related** | [Decisions](decisions.md), [Boxes and donations](boxes-and-donations.md), [Customs declarations](customs-declarations.md) |

> **Reliability.** No primary legal text (Cabinet resolutions, the Customs Code) could be read in full. Several
> primary pages returned empty content or HTTP 403/404, so most claims rest on secondary summaries and Ukrainian
> government news pages. Rules have changed repeatedly since 2022, so anything dated before 2025 should be re-checked
> against good.gov.ua and a Ukrainian customs broker before it drives a design.
>
> Claims that could not be verified are marked **UNVERIFIED**. Each number in square brackets, such as
> [[1]](#src-1), links to the matching entry in [Sources](#sources).

## Terms used

| Term | Meaning |
|---|---|
| **Recipient** | The Ukrainian legal term for the body that receives humanitarian aid. In Freedom that body is a **Receiver** ([Boxes and donations](boxes-and-donations.md#receivers-and-destinations)). This document keeps "recipient" where it quotes Ukrainian rules. |
| **AHARS** | The Automated System of Registration of Humanitarian Aid, at good.gov.ua. Where a recipient lists the goods and receives a unique code. |
| **Unique code** | The code AHARS issues for one goods list, quoted at customs clearance. |
| **Goods list** | The itemised list of aid entered in AHARS. In Freedom it is one list per Receiver, per vehicle, per convoy ([Customs declarations](customs-declarations.md#what-a-declaration-is)). |
| **UKTZED** | Ukraine's commodity classification code for goods in foreign trade. Required for every line of a goods list. |
| **EDRPOU** | The registration number of a legal entity in Ukraine. |
| **QES** | Qualified electronic signature, used to sign in to AHARS. |
| **CMU** | The Cabinet of Ministers of Ukraine. A "CMU Resolution" is one of its decrees. |
| **ADR** | The European agreement on the international carriage of dangerous goods by road. |
| **NCTS, T1, TIR, ATA** | Transit arrangements. NCTS is the electronic Common Transit system and T1 a transit declaration. TIR and ATA are carnets, which are guarantee documents for goods moving through several countries. |
| **GMR, ENS, ELO** | The UK, EU and French declarations Freedom handles. See [Customs declarations](customs-declarations.md). |
| **Dual-use** | Goods with both civil and military uses, which may need an export licence. |

## How this research is used

| Finding | Used in |
|---|---|
| A registered Ukrainian recipient files the goods list, and no API route was found | [D4](decisions.md#d4), [D12](decisions.md#d12), [D20](decisions.md#d20), [X5](decisions.md#x5), [X6](decisions.md#x6) |
| Minimum residual shelf life (one third for food; half or six months for medicines) | [D25](decisions.md#d25) |
| Gas, flammables and similar are prohibited on the border side | [D21](decisions.md#d21) |
| Code valid about 90 days, declaration about 30 days | [D34](decisions.md#d34), [D37](decisions.md#d37) (unverified) |
| No amendment route for a filed goods list | [D24](decisions.md#d24), [D31](decisions.md#d31), [D32](decisions.md#d32) |
| Vehicle import has its own rule | [D23](decisions.md#d23), [D33](decisions.md#d33) (Receiver detail is out of scope) |

## Summary

- Since 1 December 2023, goods only qualify as humanitarian aid (and so only get the duty/VAT exemption) if a registered Ukrainian **recipient** declares them in the **Automated System of Registration of Humanitarian Aid (AHARS, good.gov.ua)**. The system issues a **unique code** per goods list, which is quoted at customs clearance. Paper declarations ended on 1 April 2024. [[1]](#src-1)[[2]](#src-2)[[3]](#src-3)[[5]](#src-5)
- The recipient must be an entity in Ukraine (non-profit organisation, charity, state or communal body, local authority, accredited foreign charity representative office, defence bodies). A UK charity cannot itself be the recipient unless it has an accredited Ukrainian representative office (UNVERIFIED for Ukrainian Action). [[3]](#src-3)[[4]](#src-4)
- The declared list is itemised: UKTZED code, quantity, weight and value per item, plus donor details. Code validity is about 90 days, the customs declaration about 30 days, and the recipient must report distribution (a reporting window of up to 90 days is cited; sources differ on the detail). [[5]](#src-5)[[6]](#src-6)[[7]](#src-7)
- Goods must show a minimum residual shelf life at the border (food: at least one third; medicines/devices under a year total life: at least half; one year or more: at least six months), though martial-law relaxations for medicines are reported. [[8]](#src-8)[[9]](#src-9)
- Vehicles can be imported as humanitarian aid only under the conditions of the vehicle import rule: the declaration states the onward transfer, and handover follows within 90 days with a transfer act. Whether a given recipient qualifies is a matter for the Receiver, not this software. [[10]](#src-10)
- Dangerous goods (gas canisters, flammables), weapons, and controlled items (body armour, drones, night vision) are restricted on the EU/Polish/Hungarian exit side and have separate Ukrainian treatment. [[11]](#src-11)[[12]](#src-12)[[13]](#src-13)
- UK side: a simplified HMRC export easement existed for Ukraine aid (excludes controlled and dual-use goods); EU transit is likely required for onward movement. Ukraine joined the Common Transit Convention on 1 October 2022. [[14]](#src-14)[[15]](#src-15)

## Findings

### 1. Legal status, recipients, exemptions

- The Law of Ukraine "On Humanitarian Aid" governs; recognition as humanitarian aid is the key condition for exemption from import duty and VAT. [[5]](#src-5)[[16]](#src-16)
- Resolution 174 (1 March 2022), the early martial-law declarative regime (declaration by the person transporting goods, paper or electronic), is **no longer in force from 1 December 2023**; it was replaced by Cabinet Resolution 953 (5 September 2023, "Some issues of passing and accounting for humanitarian aid in martial law"). [[17]](#src-17)[[5]](#src-5)
- Under the law that took effect 23 January 2024, eligible recipients include public associations (non-profit), non-profit state/communal enterprises, local self-government, medical and social providers, security and defence bodies, and foreign humanitarian organisations and their representatives in Ukraine. [[4]](#src-4) Another source says recipients must have a Ukrainian legal presence, with foreign charities eligible through accredited representative offices. [[3]](#src-3)
- Donors may be legal entities or individuals. [[5]](#src-5) Charities may buy goods abroad and declare them as humanitarian aid if distributed free within 90 days of crossing. [[4]](#src-4)
- Exemption from duty and VAT follows recognition as humanitarian aid; humanitarian aid is currently not subject to non-tariff regulation measures. [[5]](#src-5) The 2026 EU decision reported by one blog (see Open questions) concerns EU-side import relief, not Ukrainian import, and is UNVERIFIED.
- Sanction for non-compliance: a foundation that breaches reporting requirements loses recipient status for six months. [[6]](#src-6)
- Whether the "Unified Register of Recipients" still requires pre-approval or has reverted to declarative status after the transitional period: sources conflict (the 2024 law gave a declarative basis for three months after martial law ends; the AHARS regime requires QES login and EDRPOU). UNVERIFIED current state. [[4]](#src-4)[[6]](#src-6)

### 2. Documents and declarations for a road convoy

- Recipient registers in AHARS with a qualified electronic signature (QES) and EDRPOU, enters the goods list in an electronic cabinet, and receives a unique code. After the code exists, **either the recipient or the donor** fills the customs declaration quoting the code. [[2]](#src-2)[[3]](#src-3)[[6]](#src-6)[[7]](#src-7)
- Validity: unique code 90 days from creation; customs declaration 30 days from creation; a source also gives a 30-day window from cargo registration to border crossing. [[7]](#src-7)[[6]](#src-6)
- Users without QES/BankID/MobileID (including non-residents) can file an electronic *notification* instead of a declaration. [[3]](#src-3)
- Neighbouring-state documents seen in practice: Hungary's customs authority says shipments need a **donation letter** and an **itemised goods list**, with the AHARS-generated code for Ukrainian entry. [[13]](#src-13)
- Whether a UK charity can file itself: the donor may fill the declaration after the recipient gets the code [[7]](#src-7); the recipient must be Ukrainian [[3]](#src-3)[[4]](#src-4). Direct filing by Ukrainian Action as donor appears possible, but the recipient prerequisite means a Ukrainian Receiver is essential. Treat as UNVERIFIED in detail.
- Pre-arrival vs at border: code is obtained before travel, declaration within 30 days; both precede the crossing. [[6]](#src-6)[[7]](#src-7)

### 3. Per item or per box

- Required per goods line in the declaration: categories/list, volumes, **UKTZED code**, quantity, weight, value per item; donor organisation registration details. [[6]](#src-6)
- Reports are generated automatically from the declaration, recipients shown by settlement only. [[6]](#src-6)[[18]](#src-18)
- Item-level (itemised) lists are expected; Hungarian guidance asks for an itemised goods list and donation letter. [[13]](#src-13) Whether box-level packing lists are mandatory in Ukraine's rules is **UNVERIFIED**; the declaration is per goods list, not per box.
- Value: required per item. Whether free-of-charge donated goods may be declared at estimated or nominal value is UNVERIFIED.

### 4. Restricted or controlled goods

- **Shelf life** (State Customs Service reminder dated 18 May 2026, citing Article 9 of the Law and CMU Resolution 728 of 28 April 2000): food and limited-life goods at least one third of manufacturer term at crossing; medicines, devices, veterinary drugs with total life under a year, at least half; one year or more, at least six months. Non-compliant goods are barred from crossing. [[8]](#src-8) A separate summary says the minimum shelf-life requirement for medicines is waived under martial law for stock that did not expire during martial law; that conflicts in part with the 2026 customs reminder and is UNVERIFIED as current. [[9]](#src-9)
- **Medicines and medical devices**: also governed by the Ministry of Health regime; wartime flexibilities exist. Details UNVERIFIED beyond shelf life. [[9]](#src-9)
- **Dangerous goods (gas canisters, flammables, batteries)**: sources on the border side state flammable, explosive, corrosive substances and pressurised gases are prohibited for aid consignments, and ADR classification applies. [[11]](#src-11)[[13]](#src-13) Lithium batteries specifically: UNVERIFIED. Ukrainian-side treatment of gas canisters as humanitarian aid: UNVERIFIED.
- **Weapons and ammunition**: cannot be sent as aid consignments. [[11]](#src-11)[[13]](#src-13)
- **Controlled equipment**: Hungary allows body armour, helmets, drones and civil explosives only with authorisation and always with a written declaration. [[13]](#src-13) On the Ukrainian side, drones and their parts, helmets and body armour to NATO specifications were simplified for import during martial law; the end-user guarantee letter and dual-use drone licences were waived in early 2023; later cases moved onto the AHARS code. [[19]](#src-19)[[20]](#src-20) Night vision, thermal imagers and rangefinders were exempted from duty and VAT by laws in 2023 to 2025. [[21]](#src-21) Whether these items travel as humanitarian aid or military supply, and whether the UK side requires export licences (dual-use), is UNVERIFIED; the UK easement explicitly excludes controlled and dual-use goods. [[14]](#src-14)
- **Vehicles**: since 1 December 2023 NGOs and foundations lost simplified import for their own use; vehicles can be imported as humanitarian aid only under a dedicated vehicle rule. The declaration must state the onward transfer, with handover within 90 days and a transfer act. [[10]](#src-10) From 23 January 2024 the set of entities able to import vehicles widened. [[4]](#src-4)[[10]](#src-10) Which recipients qualify is for the Receiver to determine and is out of scope for this software. UNVERIFIED in detail. [[10]](#src-10)

### 5. Changes before the border (contents change, breakdown)

- Searches found no source describing amendment of a submitted AHARS list or declaration, or transfer of goods between vehicles. **UNVERIFIED.**
- Known constraints that bear on it: the unique code is tied to a goods list valid ~90 days; the declaration is valid 30 days. [[7]](#src-7) Practical inference only (not sourced): a materially changed load would need a revised or new goods list/code from the recipient, and transhipment in the EU may need transit documents rather than a customs change. Confirm with the recipient and a broker.

### 6. Electronic systems and APIs

- **AHARS / good.gov.ua**, operated under the State Customs Service ("Ukrainian State System for Humanitarian Aid" at customs.help.gov.ua): electronic cabinet; login via QES, BankID or MobileID; English interface and user guide. [[3]](#src-3) The "Single Window for International Trade" portal is the other route for filing. [[1]](#src-1)
- The law only says the Cabinet sets up the automated system; no vendor is named. [[4]](#src-4)
- **API or third-party submission: no evidence found. UNVERIFIED.** Pages reviewed list no integration options. [[3]](#src-3)
- Poland's side uses AES/ECS2 electronic export declarations in XML for aid, except small consignments, with humanitarian coordinators in revenue administration chambers (2022 guidance, may be outdated). [[22]](#src-22)

### 7. Data sensitivity

- Reports show recipients by settlement only, not detailed addresses. [[6]](#src-6)[[18]](#src-18) This is consistent with the repository's rule that delivery addresses never travel; no source says a delivery address must appear on declarations, but recipient registration data (organisation, EDRPOU) is required. [[6]](#src-6) Whether a named end beneficiary or address must appear on the border documents is UNVERIFIED.
- No source reviewed addresses Ukrainian rules on publishing recipient details. UNVERIFIED.

### 8. UK to Ukraine specifics

- **UK export**: gov.uk guidance created an easement letting charities declare orally or by driving through a GB port, with no electronic HMRC export declaration, for goods for victims in Ukraine not routed via Russia or Belarus. It excludes controlled and dual-use goods. It recommended donating money instead of driving goods. [[14]](#src-14) Date and current status (2026) of the easement: UNVERIFIED. Re-check on gov.uk.
- **EU leg**: member states may offer easements (the Netherlands simplified procedures for goods from the UK, free-of-charge aid presented by declaration); transit may be needed for onward delivery. [[14]](#src-14) A UK charity moving goods through France, Germany and Poland needs transit cover for non-EU goods; check with carriers. [[14]](#src-14)[[15]](#src-15)
- **Common Transit**: Ukraine joined the Common Transit Convention on 1 October 2022 and uses NCTS Phase 5; this lets goods move under transit into Ukraine. [[15]](#src-15)[[23]](#src-23) Whether the convoy actually uses T1/NCTS, TIR or an ATA carnet: UNVERIFIED for this charity.
- **Poland**: road aid usually undergoes export procedures at designated internal customs offices, not at the border crossing; humanitarian coordinators exist (2022). [[22]](#src-22)
- **Hungary**: pre-notification of arrival time, cargo and crossing point is recommended; written declarations always required for restricted goods; exit over €1,000 or 1,000 kg needs a written, usually electronic, declaration; some crossings have limited hours. [[13]](#src-13)
- Slovakia, Romania, Moldova routes: UNVERIFIED, not researched.

## Implications for box and item data

The Ukrainian declaration is a goods list, **not a per-box record**. In Freedom it is one list per Receiver, per vehicle, per convoy ([X6](decisions.md#x6)). Box-level capture should be rich enough to roll up into that list and to check shelf life and restricted categories.

> **Sensitivity.** Rows that describe a Receiver's registration (below) are Receiver data. Under [D33](decisions.md#d33) the software does not document or codify what kind of body a Receiver is, so those rows are **not adopted** until they have had a data-sensitivity review.

| Level | Field | Why | Source |
| --- | --- | --- | --- |
| Item | Description (declared name) | Goods list line | [[6]](#src-6) |
| Item | UKTZED code (10 digit) | Required per line | [[6]](#src-6) |
| Item | Quantity and unit | Required per line | [[6]](#src-6) |
| Item | Unit and total value (donated value basis) | Required per line | [[6]](#src-6) |
| Item | Net weight | Required per line | [[6]](#src-6) |
| Item | Category (food, medicine, medical device, clothing, tool, battery, gas, other) | Drives shelf life and restriction rules | [[8]](#src-8)[[11]](#src-11) |
| Item | Manufacturer shelf-life term, expiry date, residual fraction at planned crossing | Minimum residual shelf life | [[8]](#src-8) |
| Item | Dangerous goods flag, ADR class, UN number (batteries, gas) | ADR and prohibited-goods checks | [[11]](#src-11)[[13]](#src-13) |
| Item | Controlled-goods flag (body armour, helmets, drones, optics, radios) and licence/authorisation reference | Border authorisation | [[13]](#src-13)[[14]](#src-14) |
| Item | Vehicle VIN, plate | Vehicle import rule | [[10]](#src-10) |
| Box | Box id, gross and confirmed weight, validation state | Weight check and Freedom's own manifest | [Boxes and donations](boxes-and-donations.md#purpose-of-a-box) |
| Box | Link to goods-list line(s), packed-from-donor reference | Roll-up to declaration | [[6]](#src-6) |
| Box | Donor (organisation or individual) | Donor details in declaration | [[5]](#src-5)[[6]](#src-6) |
| Box | Receiver organisation and region only (no street) | Reports by settlement; data sensitivity | [[6]](#src-6)[[18]](#src-18) |
| Convoy | Ukrainian recipient organisation, EDRPOU, AHARS registration — **not adopted, see sensitivity note** | Recipient prerequisite | [[3]](#src-3)[[6]](#src-6) |
| Convoy | AHARS unique code, creation date, expiry (90 days) | Quoted at clearance | [[7]](#src-7) |
| Convoy | Customs declaration reference, creation date, expiry (30 days) | Validity window | [[7]](#src-7) |
| Convoy | Border crossing point and planned date; transit mode and reference (T1/NCTS, TIR, ATA) | Routing and pre-notification | [[13]](#src-13)[[15]](#src-15) |
| Convoy | UK export evidence and EU member-state easements used | UK/EU legs | [[14]](#src-14) |
| Convoy | Donation letter and itemised list generated | Documents carried | [[13]](#src-13) |
| Convoy | Distribution report status and due date | Recipient reporting | [[6]](#src-6) |
| Manifest | Final delivered, lost, returned status per item | Feeds recipient report | [P5](decisions.md#p5), [[6]](#src-6) |

## Open questions and unverified claims

1. Is Ukrainian Action, or any of its Receivers, an AHARS-registered recipient or accredited representative office? This decides who can create the unique code. UNVERIFIED.
2. Current recipient eligibility: transition after martial law and the declarative-status provisions. UNVERIFIED as current in October 2026. [[4]](#src-4)
3. Amendments and transhipment: how to change an AHARS list, whether a new code is needed, what happens when goods move between vehicles after a breakdown. UNVERIFIED.
4. API or bulk upload into AHARS: none found. Ask the State Customs Service or the AHARS help desk.
5. Is box-level data required anywhere, or only the goods list? UNVERIFIED.
6. Value basis for donated goods (market value, nominal, zero). UNVERIFIED.
7. Gas canisters, lithium batteries: legal carriage via road under ADR and acceptance in Ukraine as aid. UNVERIFIED; the Polish and Hungarian border-side texts treat gas and flammables as prohibited. [[11]](#src-11)[[13]](#src-13)
8. Medicines: martial-law relaxation versus the May 2026 customs reminder on shelf life; need for Ministry of Health or importer registration. [[8]](#src-8)[[9]](#src-9)
9. Vehicles: the exact conditions of the vehicle import rule and the handover act, to be confirmed by the Receiver. [[10]](#src-10)
10. Dual-use and export licensing on the UK side for drones, optics, radios, body armour. UNVERIFIED.
11. One blog reports Commission Decision (EU) 2026/1407 (C(2026)4366, in force 26 June 2026) giving EU tariff and VAT relief for humanitarian goods to Ukraine. I could not confirm it on EUR-Lex; UNVERIFIED and it relates to EU import, not Ukrainian import. [[24]](#src-24)
12. The UK export easement and the Dutch and other EU easements were 2022 measures; current status UNVERIFIED. [[14]](#src-14)
13. Slovakia, Romania and Moldova routes not researched.
14. Data sensitivity: nothing found on whether Ukrainian rules or border documents require or publish a recipient address.

## Sources

Retrieved 2026-10-01 unless stated. Many pages could be read only in part; secondary sources are marked.

1. <a id="src-1"></a>https://lca.logcluster.org/ukraine-13-customs-information (search summary only; page text did not load) , retrieved 2026-10-01. Secondary.
2. <a id="src-2"></a>https://www.kmu.gov.ua/en/news/v-ukraini-1-hrudnia-zapratsiuie-tsyfrovyi-mekhanizm-dlia-vvezennia-humanitarnoi-dopomohy , Cabinet of Ministers news on the 1 December 2023 digital mechanism, retrieved 2026-10-01.
3. <a id="src-3"></a>https://customs.help.gov.ua/en/ , Ukrainian State System for Humanitarian Aid, retrieved 2026-10-01.
4. <a id="src-4"></a>https://chamber.ua/news/a-new-procedure-for-importing-accounting-and-distributing-humanitarian-aid/ , American Chamber of Commerce in Ukraine on the January 2024 law, retrieved 2026-10-01. Secondary.
5. <a id="src-5"></a>https://www.lexology.com/library/detail.aspx?g=500d59fd-89dc-427a-8fa8-7fa032c02053 and the search results for Resolution 953 (https://latvia.mfa.gov.ua/en/news/new-rules-importing-humanitarian-aid-ukraine), retrieved 2026-10-01; both pages returned 403 on fetch, so claims come from search-result summaries only. Secondary.
6. <a id="src-6"></a>https://visitukraine.today/blog/3650/new-rules-for-importing-humanitarian-aid-to-ukraine-registration-algorithm-and-reporting-in-the-electronic-system , retrieved 2026-10-01. Secondary.
7. <a id="src-7"></a>Search results quoting the AHARS guidance (code valid 90 days, declaration 30 days), via https://finland.mfa.gov.ua/en/news/INSTRUCTION-humanitarian-aid-ukraine (403 on fetch) and https://help.gov.ua/en (not fetched), retrieved 2026-10-01. Secondary, search summary only.
8. <a id="src-8"></a>https://en.interfax.com.ua/news/general/1168744.html , State Customs Service shelf-life reminder, dated 18 May 2026, retrieved 2026-10-01.
9. <a id="src-9"></a>https://www.lexology.com/library/detail.aspx?g=57a3bd2b-16f8-45aa-8ddb-8de866266cc6 and https://ukrainianlawfirms.com/wartime-flexibilities-for-the-life-sciences-and-healthcare-sectors-in-ukraine/ (search summary only; fetch returned 403 or not attempted), retrieved 2026-10-01. Secondary.
10. <a id="src-10"></a>https://eauto.org.ua/en/news/426-since-december-1-the-rules-for-the-import-of-humanitarian-aid-have-changed-how-to-bring-a-car-of-the-zsu-now and https://wah.ua/en/blog/128-changes-to-the-law-on-importing-cars-as-humanitarian-for-the-armed-forces-of-ukraine (search summaries only), retrieved 2026-10-01. Secondary, commercial.
11. <a id="src-11"></a>https://nav.gov.hu/en/main-tiles/aid-consignments-to-ukraine for prohibited categories on the Hungarian side; the wider prohibited-goods list in the search result at https://dragonlogistics.com.ua/en/prohibited-imports-ukraine-what-you-need-to-know/ (search summary only), retrieved 2026-10-01.
12. <a id="src-12"></a>https://www.kmu.gov.ua/en/news/sproshcheno-poriadok-vvezennia-na-mytnu-terytoriiu-ukrainu-droniv-kasok-i-bronezhyletiv , simplified import of drones, helmets, body armour (search summary only), retrieved 2026-10-01.
13. <a id="src-13"></a>https://nav.gov.hu/en/main-tiles/aid-consignments-to-ukraine , Hungarian customs on aid consignments to Ukraine, retrieved 2026-10-01.
14. <a id="src-14"></a>https://www.gov.uk/guidance/taking-humanitarian-aid-out-of-great-britain-to-support-ukraine , UK Government guidance (search summary only; page not fetched), retrieved 2026-10-01.
15. <a id="src-15"></a>https://taxation-customs.ec.europa.eu/news/customs-ukraine-join-common-transit-convention-and-convention-simplification-formalities-trade-goods-2022-09-05_en , European Commission on Ukraine joining the Common Transit Convention on 1 October 2022 (search summary only), retrieved 2026-10-01.
16. <a id="src-16"></a>https://www.icnl.org/research/library/ukraine_human/ , Law of Ukraine on Humanitarian Aid (not fetched; search listing only), retrieved 2026-10-01.
17. <a id="src-17"></a>https://zakon.rada.gov.ua/laws/show/174-2022-%D0%BF?lang=en , Resolution 174 status page (metadata only: no longer in force from 1 December 2023, replaced by 953-2023-p), retrieved 2026-10-01.
18. <a id="src-18"></a>https://www.kmu.gov.ua/en/news/v-ukraini-1-hrudnia-zapratsiuie-tsyfrovyi-mekhanizm-dlia-vvezennia-humanitarnoi-dopomohy , automated reports from declarations, retrieved 2026-10-01.
19. <a id="src-19"></a>https://www.kmu.gov.ua/en/news/sproshcheno-poriadok-vvezennia-na-mytnu-terytoriiu-ukrainu-droniv-kasok-i-bronezhyletiv (search summary only), retrieved 2026-10-01.
20. <a id="src-20"></a>https://voxukraine.org/en/how-ukrainian-charity-foundations-purchased-drones (search summary only), retrieved 2026-10-01.
21. <a id="src-21"></a>https://kyivindependent.com/zelensky-signs-law-exempting-anti-drone-hardware-and-other-related-military-hardware-from-customs-duties-and-vat/ and https://www.economicleadership.institute/en/drones-thermal-imagers-and-walkie-talkies-were-exempted-from-vat-and-customs-duties/ (search summaries only), retrieved 2026-10-01.
22. <a id="src-22"></a>https://granica.gov.pl/j/index.php/en/notice-2/875-rules-concerning-the-humanitarian-aid-consignments-for-ukraine , Polish customs, published 4 March 2022, retrieved 2026-10-01. Possibly outdated.
23. <a id="src-23"></a>https://mof.gov.ua/en/news/ukrainian_business_is_among_the_first_20_countries_out_of_36_to_use_the_latest_version_of_the_ncts_phase_5_common_transit_system_the_so-called_customs_visa-free_regime-4759 , Ukraine on NCTS Phase 5 (search summary only), retrieved 2026-10-01.
24. <a id="src-24"></a>https://cambioslegales.es/en/blog/tariff-exemption-vat-humanitarian-aid-ukraine-2026 , law-firm blog claiming Commission Decision (EU) 2026/1407, retrieved 2026-10-01. Not corroborated; treat as UNVERIFIED.
25. <a id="src-25"></a>https://mssdefence.com/blog/is-ukraines-resolution-953-hurting-help-for-war-victims/ , commentary on Resolution 953 (QES signature, 30-day registration, 90-day reporting, November 2023), retrieved 2026-10-01. Secondary.

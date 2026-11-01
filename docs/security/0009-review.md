# Security review: the Convoy Leader reads destination addresses

Date: 2026-11-01. Gate for [plan 19](../plans/19-leader-address-access.md), Increment 0, and for
[ADR 0009](../adr/0009-convoy-leader-reads-destination-addresses.md)
([X7](../domain/decisions.md#x7), [X9](../domain/decisions.md#x9), [X11](../domain/decisions.md#x11),
[X12](../domain/decisions.md#x12), [X13](../domain/decisions.md#x13), [O1](../domain/decisions.md#o1),
[O26](../domain/decisions.md#o26)). Status: **proposed, awaiting owner and security-reviewer sign-off. No code has
changed.**

Reviewed against [Data Sensitivity](../domain/key-concepts.md#data-sensitivity) and
[recommendations section 4.4](../recommendations.md#44-treat-ukrainian-delivery-detail-as-the-most-sensitive-data-in-the-system).

## 0. Summary and recommendations

| # | Recommendation | Section |
|---|---|---|
| R1 | **Fix first, independent of this plan:** `dbo.ConvoyRouteStop` holds `House`, `Street`, `City`, `Postcode` outside the `sensitive` schema, readable by every operational role. Decide what a route stop may hold before the leader path is built. | 4 |
| R2 | Use a **narrower database principal** (`convoy_leader_reader`) that can execute one stored procedure filtered by convoy and window, not the `ground_officer` principal. | 6 |
| R3 | **Re-authenticate** (`prompt=login`, `max_age=300`) before the first reveal, and again after a **10 minute idle** or after the page is hidden. | 3 |
| R4 | Reveal **per Receiver, on request**, not the whole list at once; each reveal is one audit row. | 3, 5 |
| R5 | The window check, the leadership check and the audit write happen **inside the database call**, not only in C#. | 6 |
| R6 | Accept, in writing, that screenshots, browser history of the page and a photograph of the screen cannot be prevented. | 7 |
| R7 | Reassigning the leader ends access on the **next request**, because scope is decided from the assignment on every call. Sign-out of the old leader's session is not required for that to hold. | 1, 3 |

## 1. Threat model

The reader is a volunteer driver on a personal phone, in a vehicle that crosses several borders. The system-side
controls (policy, scope, audit, window) work on the server. None of them helps once an address is on a screen.

| Threat | What it can reach | Existing control | Gap | Mitigation proposed |
|---|---|---|---|---|
| **Lost or stolen phone** | Whatever the page shows now, plus any live session | Access token 15 min; address held in memory only (X10); `no-store` | The SSO session idles for **8 h** and `automaticSilentRenew` is on, so a thief with an unlocked browser keeps getting tokens (`web/src/auth/oidcConfig.ts`, `iac/tofu/keycloak.tf`) | R3: addresses require a fresh login (`max_age=300`), so a stolen session cannot reveal them; idle timeout clears them from memory |
| **Shoulder surfing** | What is visible on the screen | None possible | Whole list visible at once would show every Receiver | R4: reveal one Receiver at a time, with a visible **Hide** control; hide on blur/visibility change |
| **Leader detained at a border with an open session** | Whatever is revealed, and the ability to reveal more | Window ends at arrival | Detention happens *before* arrival, inside the window | R3 idle timeout; a **panic "hide all and sign out"** control on the page; the Dispatcher can reassign, which ends access on the next request (R7). Accepted: an officer holding an unlocked phone sees what is revealed |
| **Reassigned leader** | Their old convoy's addresses | Scope is read from `ConvoyLeaderAssignment` on every request (plan 17); `LeaderRoleClaims` is derived per request | A page already open keeps the addresses in memory | Server refuses the next fetch with 403; the page treats any 403 as "hide everything". Residual: what was already on the screen |
| **Leader also on a later convoy** | Addresses of convoy B while leading A | Scope is per convoy id | Window is per convoy: leading B opens B's window 14 days before B departs | `IsOpen` takes the convoy being read; no cross-convoy inference. Test: leader of A and B, B's window closed |
| **Insider Dispatcher who reassigns themselves** | Addresses of any convoy, by nominating themselves | `convoys:lead-assign` is Administrator/Dispatcher; the assignment history is kept; `LastChangedBy` is stamped | A Dispatcher can become a leader (X12's scoping is only as strong as nomination) | Audit row records the **capacity** and the assignment id; a nomination made **inside the window** raises a visible task for an Administrator; a Dispatcher cannot nominate themselves (Administrator only for self-nomination). **Owner decision needed (D1)** |
| **A compromised leader token used from another device** | Same as the leader | 15 min token, TLS | Token replay for up to 15 min | R3: reveal needs a recent *authentication*, not just a valid token (`auth_time` claim checked server-side) |
| **Insider with database access** | `sensitive.ReceiverDetail` directly | `DENY SELECT` to the app; separate principals | Unchanged by this plan | Out of scope; recorded for completeness |

## 2. The wider scope (O26)

O26 gives the leader **every Receiver on the convoy** (each box's and each vehicle's handover Receiver), not only a
final destination.

**Quantification.** The repository holds no production data, so these are planning figures to be confirmed by the
owner: a convoy is of the order of **10 to 20 vehicles** with **1 to 3 handover Receivers** each, and boxes mostly
follow their vehicle's Receiver. Plan for **roughly 5 to 15 distinct Receivers per convoy**, each with a street
address and a named contact with a phone number. That is 5 to 15 addresses and as many named individuals in
Ukraine, held on one phone, for up to 14 days plus the journey.

The seed (`database/seed/dev-seed.sql`, 178 lines) is a development fixture and is not a basis for the figure.

**Assessment.** The widening is the largest part of the risk, because the loss of one phone discloses all the
Receivers at once, where X9 would have disclosed the destinations the convoy reaches. Mitigations: R4 (one at a
time), and **contact name and phone are a separate reveal from the address** (the leader needs the address to drive
and the phone to arrive; they need not be on screen together). **Owner decision (D2):** confirm the wider scope
stands, or limit the list to Receivers the convoy actually delivers to (those with a box or vehicle not yet
delivered), which shrinks the list as the journey progresses.

## 3. Session controls

Today (`web/src/auth/oidcConfig.ts`, `iac/tofu/keycloak.tf`): authorization-code flow, tokens held in memory only,
`automaticSilentRenew: true`, access token **15 min**, SSO session idle **8 h**.

| Control | Proposal |
|---|---|
| Re-authentication before reveal | The first reveal in a page session redirects through the identity provider with `prompt=login` and `max_age=300`. The API checks the token's `auth_time` is within 5 minutes and answers `401 reauthentication-required` otherwise. A silent renew does not satisfy it. |
| Idle timeout | After **10 minutes** without interaction, or when the page is hidden (`visibilitychange`), every revealed address is cleared from memory. Revealing again needs the fresh-authentication check, which will pass for the first 5 minutes only. |
| Session lifetime | Leave the 8 h SSO idle as is for the rest of the application, but the reveal does not rely on it. Not reducing it avoids making the checklist unusable on a long drive. **Owner decision (D3).** |
| Sign-out on reassignment | Not required for the guarantee: access is refused server-side on the next request (R7). The page hides everything on any 401/403. A back-channel logout is not proposed. |
| Navigation away | Addresses live in component state only and are cleared on unmount, on sign-out and on a `pagehide`. |

## 4. Stores: which reads come from where

| Data | Store | Read by | Notes |
|---|---|---|---|
| Receiver address, contact name, phone | `sensitive.ReceiverDetail` | `ReceiverDetailRepository` only, as `freedom_sensitive` (`ground_officer`) | The only copy that is meant to exist |
| Route point name and kind | `dbo.ConvoyRouteStop` (`Name`, `Kind`, `Authority`) | All operational roles; the leader before the window (X13) | Headers. Safe by design |
| **Route point `House`, `Street`, `City`, `Country`, `Postcode`** | **`dbo.ConvoyRouteStop`, schema `dbo`** | `freedom_app` (`SELECT` on `dbo`) and every operational role | **FINDING.** See below |
| Handover and box Receiver | `dbo.Receiver` (reference, organisation, region, status), `dbo.ConvoyVehicle.HandoverReceiverRef`, `dbo.Box.ReceiverRef` | Operational roles | No address, by type (`ReceiverReadModel`). Safe |

### Finding F1: route points carry addresses outside the `sensitive` schema

`dbo.ConvoyRouteStop` has `House`, `Street`, `City`, `Country`, `Postcode`. They are readable by `freedom_app` and by
every role granted `SELECT` on `dbo`, and returned by `GET /convoys/{id}/route` to every operational caller and the
leader (`convoys:read-led`). The comment on the table says a stop is "written by a dispatcher planning a journey".
For a UK pick-up, a ferry port or a hotel that is harmless. For a **Ukrainian stop that is a Receiver's premises**, it
is a delivery address held in the ordinary schema, bypassing all three controls in ADR 0009's context. That would be
a hole independent of this plan, and it would make the leader path pointless as a control, since the same address
would already be readable under `convoys:read-led` before the window opens, contradicting X13.

**Recommendation (R1), owner to choose (D4):**

1. **Preferred:** a route point carries **no street address** for a point in Ukraine (`Authority = 2`, or `Country`
   Ukraine): `Name`, `Kind`, `Authority`, `Country`, `CountryCode` only. The address of a destination lives only as
   the Receiver's detail, and a route point that is a delivery names the Receiver (`ReceiverRef`) rather than
   carrying text. Enforced by the API validator and a `CHECK`, with a migration-free end-state schema as the project
   requires.
2. Alternative: move `House`, `Street`, `Postcode` of Ukrainian stops into `sensitive` behind the same path as
   Receiver detail.

Either must land **before** the window is meaningful. It is a change to plan 10's slice, so it is listed here as a
prerequisite and not folded into plan 19 silently.

**Not found:** the Customs Worker queues, the manifest document request, the box label and the ENS filing sheet carry
no Receiver address by type (see 7). The access log stores a Receiver reference only.

## 5. Audit

`sensitive.ReceiverDetailAccessLog (Id, ReceiverRef, PersonId, ReadAt, Reason)` is written in the same transaction as
the read. It has no foreign keys, deliberately, so it outlives the address.

**Proposed additions** (columns of that table, `NOT NULL` where the path always knows them):

| Column | Meaning |
|---|---|
| `Capacity` (`GroundOfficer` or `ConvoyLeader`) | In what capacity the reader acted |
| `ConvoyId` (nullable, only for a leader) | Which convoy the read was for |
| `LeaderAssignmentId` (nullable) | The assignment relied on, so a later question "were they actually the leader?" has an answer |
| `Field` (`Address` or `Contact`) | Which part was revealed (R4) |

What a leader's read records: **who** (`PersonId`), **which convoy**, **which Receiver**, **in what capacity**, **under
which assignment**, **what part** and **when**. A denied attempt (outside the window, not the leader) is logged too
by the procedure, as the Ground Officer path already logs an attempt that finds nothing.

**Review.** A Ground Officer or Administrator reads the log. Today there is `CountAccessesAsync` only and no list
endpoint. Proposed: `GET /receivers/{ref}/access-log` under `receivers:detail` (Ground Officer) listing capacity,
convoy and time, never the reader's name beyond `PersonDisplay`. **Owner decision (D5):** should an Administrator
(who is excluded from `receivers:detail`) also see it, given the isolation rule runs both ways?

**Retention.** [Q-retention](../domain/decisions.md#q-retention) is still open. The log is kept until it is decided;
nothing in this plan deletes it. Flagged, not resolved.

## 6. Database identity

Options:

| Option | Pros | Cons |
|---|---|---|
| A. Reuse `freedom_sensitive` (`ground_officer`) | No new principal; the path and the audit already exist | The leader path then holds the **whole** `sensitive` schema `SELECT`, including `UPDATE`/`DELETE`. A bug in a leader query can read any Receiver, or any other sensitive table added later, and the audit is only what the C# remembers to write |
| B. A new principal `convoy_leader_reader` with `EXECUTE` on one procedure and no table rights | The database refuses everything else, so a bug cannot widen the read; the convoy filter, window and audit are inside the procedure (R5) | A new principal, a procedure to maintain, and the environment layer (`iac/local/sql/principals.sql`, the Azure pipeline) must create it |

**Recommendation: B.** `sensitive.ResolveLeaderReceiverDetail(@personId, @convoyId, @receiverRef, @field)`:

- joins `ConvoyLeaderAssignment` (open, for `@personId`) and checks the Receiver is on the convoy (a box or vehicle
  names it) and the window is open (`@windowDays` from configuration passed in, `Start - windowDays <= now` and
  `ArrivedAt IS NULL`);
- writes the audit row first, in the same transaction, **including when it refuses**;
- returns the one field asked for, or nothing.

The application still refuses before calling (policy, scope, `IsOpen`), so the procedure is a second, independent
check: the same "each holds if the others are removed" property ADR 0009 claims. The `dbo.Convoy*` tables the
procedure reads need `SELECT` for the procedure's owner only (ownership chaining), not for the principal.
`freedom_app` stays `DENY SELECT` on the schema and gets **no** `EXECUTE` on the procedure.

## 7. Tests that prove each control, and what cannot be prevented

| Control | Test | Layer |
|---|---|---|
| Window opens at `departure - windowDays`, ends at arrival, follows a moved departure | Table-driven `LeaderAddressAccess.IsOpen` | Unit |
| Only the current leader; another convoy's leader, a former leader, a Dispatcher, a Ground Officer via the new route, an unlinked login are refused | Policy and scope | Component |
| Before the window: headers only, never an address (X13), including on `GET /convoys/{id}/route` once F1 is fixed | Walk the JSON for address property names | Component |
| Fresh authentication required for reveal | `auth_time` older than 5 minutes gives `401 reauthentication-required` | Component |
| Read and audit row in one transaction; a refusal is audited | Row count before and after, rollback on failure | Integration |
| `freedom_app` still cannot read `sensitive`, and cannot execute the procedure | Extend `ReceiverSegregationTests` | Integration |
| `convoy_leader_reader` has no table rights, only `EXECUTE` | New segregation test, connected as that principal | Integration |
| Procedure refuses outside the window or for a Receiver not on the convoy, **with the C# check bypassed** | Call the procedure directly | Integration |
| No address in span attributes, logs or metric tags on the new route | Extend `RedactingActivityProcessor` tests and a log capture (MELT) | Component / telemetry |
| No address in a queue message, manifest document request, label or filing sheet | The label-absence shape: the types have nowhere to carry one | Unit |
| `Cache-Control: no-store` on the route, including 401/403/409 | Add to `ChecklistNoStoreMiddleware` pattern | Component |
| Page writes nothing to storage; clears on hide, idle, unmount | Vitest source scan (as plan 18 does) and behaviour tests | Vitest |
| End to end: before window, inside, other convoy, reassigned, audit visible to a Ground Officer | BDD and Playwright (mobile) | BDD / e2e |

**What cannot be prevented, and is accepted:**

- A screenshot or photograph of the screen while an address is revealed.
- The browser's own history and tab thumbnails; the URL carries no address, and the page uses no storage, but the OS
  may capture the screen for the app switcher.
- A leader who copies an address by hand, or reads it out over the radio.
- A detained leader unlocking their phone under compulsion.

The honest claim is "nothing is stored, logged, printed or queued by Freedom, and every reveal is attributed", not
"the address cannot leak".

## 8. Decisions needed from the owner

| # | Question | Recommendation |
|---|---|---|
| D1 | May a Dispatcher nominate themselves as leader? | No: self-nomination is Administrator-only, and a nomination inside the window raises a task |
| D2 | Does O26 stand for **all** Receivers, or only those still to be delivered? | Only those with undelivered cargo; the list shrinks as the journey progresses |
| D3 | Leave the 8 h SSO idle unchanged and rely on the 5 minute reveal re-authentication? | Yes |
| D4 | Fix F1 by removing street addresses from Ukrainian route points (preferred), or by moving them into `sensitive`? | Remove; the Receiver's detail is the single copy |
| D5 | Who reviews the access log: Ground Officer only, or also Administrator? | Ground Officer and Administrator (read-only, no address) |
| D6 | Adopt the narrower `convoy_leader_reader` principal and stored procedure (section 6, option B)? | Yes |
| D7 | Accept the residual risks in section 7? | Yes, in writing |

## 9. Sign-off

Plan 19 resumes on this branch only after the owner and a security reviewer record their decisions on D1 to D7 in the
PR. Outcomes are then written into ADR 0009, and `/security-review` is run again on the implementation before the
final PR is marked ready.

| Role | Name | Date | Outcome |
|---|---|---|---|
| Owner | | | |
| Security reviewer | | | |

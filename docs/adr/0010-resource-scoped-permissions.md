# 10. Permissions can be scoped to a resource: a Convoy Leader to one convoy, a Loader to their locations

Date: 2026-10-02

## Status

**Accepted** on 2026-10-04, on the owner's sign-off of the mechanism (see *Accepted mechanism* below). The need is decided ([X12](../domain/decisions.md#x12), [O14](../domain/decisions.md#o14)). Built by [plan 17](../plans/17-scoped-permissions.md), on top of the groundwork [plan 02](../plans/02-login-person-link.md) built: a login is linked to a person ([O34](../domain/decisions.md#o34)) and an unlinked login is refused on any write that records who did it ([O35](../domain/decisions.md#o35)).
mechanism below is a recommendation that has not been agreed. Not yet implemented, except the groundwork [plan 02](../plans/02-login-person-link.md) built: a login is linked to a person ([O34](../domain/decisions.md#o34)) and an unlinked login is refused on any write that records who did it ([O35](../domain/decisions.md#o35)).

## Context

Every permission in Freedom today is **global**. A person holds an application role, the identity provider puts it
in a flat `roles` claim, and each policy is a set of roles (`AddFreedomAuthorization`, with
`MapInboundClaims = false`). Entra External ID and the local Keycloak both carry roles that way. A Dispatcher can
edit any convoy, and a Loader can see any box in any location.

Two decisions need something a role cannot say:

- A **Convoy Leader** is a driver, but only **for one convoy**. They may read that convoy's addresses and no
  other, and only while they lead it ([ADR 0009](0009-convoy-leader-reads-destination-addresses.md)).
  Reassigning the leader takes the access away.
- A **Loader sees only the locations they manage** ([decision O14](../domain/decisions.md#o14)), so they cannot
  browse stock at a hub that is not theirs.

Both are "this person may do X *to this thing*". Neither is expressible as a role.

## Decision

**Keep the role as a coarse capability, and decide the resource in the application from assignment records.**

1. The token carries a coarse role, as now: a new `ConvoyLeader` capability, and the existing `Loader`.
2. The database holds the **assignments**: the convoy's leader (`dbo.Convoy` names a person, with history, per
   [decision D17](../domain/decisions.md#d17)) and a Loader-to-location assignment table.
3. A **resource-based authorization handler** checks both, the role in the token and the assignment for the
   resource named in the request, before the endpoint or handler runs.
4. **The role never reaches past the assignment.** A `ConvoyLeader` with no current convoy can do nothing.
5. An **Administrator** assigns Loaders to locations ([Q-admin-assignments](../domain/decisions.md#q-admin-assignments)),
   and a **Dispatcher or Administrator** reassigns a leader ([decision P14](../domain/decisions.md#p14)).

## Alternatives considered

**Per-resource claims in the token** (for example `leader:convoy-12`). Rejected: Entra app roles cannot express
them, the token would be stale the moment a leader is reassigned, and it hands the identity provider a job it does
not do well.

**A role per convoy created in the identity provider.** Workable at one convoy a month, and operationally heavy: an
admin step in a second system, with a failure mode where the two disagree about who the leader is.

**Check scope inside each handler.** The least machinery, and the easiest to forget: one handler that skips it is a
leak. A single authorization handler fails closed for any endpoint that declares the requirement.

## Consequences

**A login must be tied to a person.** The handler needs the caller's `Person`. `dbo.Person` holds an anonymous
`Id` and no identity-provider subject today. Whether volunteers who drive already have logins, and how a token's
subject maps to a `PersonId`, has to be confirmed. Without that mapping nothing here can be built. The `Driver`
role also has no policy today, so the leader needs a login and a role that does.

**List and search endpoints must filter, not just single reads.** The risk with scoping is not the read of one
resource but the list that returns all of them. Every box, location and bay query a Loader can reach must carry
the scope, and a test should prove a Loader cannot see another location's stock through any route.

**Role claims stay flat.** The local realm and the Entra mapping gain a `ConvoyLeader` role and nothing else, so
the authorisation policies still port to Azure app roles unchanged.

**Tests.** `TestAuthHandler` needs to express a caller's person and assignments, and the BDD logins need a Convoy
Leader and a scoped Loader beside `admin`, `operator` and `groundofficer`.

**An assignment is also an audit fact.** Who was leader when, and who managed a location when, is kept
([ADR 0017](0017-every-entity-records-its-last-change.md)).

## Amendments

Added 2026-10-02, from questions resolved before implementation planning.

- **The mechanism was Proposed until a decision checkpoint.** The first step of
  [plan 17](../plans/17-scoped-permissions.md) writes the concrete mechanism into this ADR, opens a draft PR and stops
  for the owner's sign-off. This ADR becomes Accepted then, not before.
- **A login is linked to a person** by storing the identity provider's subject on the erasable person details,
  linked by an Administrator ([decision O34](../domain/decisions.md#o34)). That resolves the first consequence above,
  and is built in [plan 02](../plans/02-login-person-link.md).
- **An unlinked login is refused (403) on any write that records who did it** ([decision O35](../domain/decisions.md#o35)).
- **The Administrator assigns Loaders to locations** ([decision O31](../domain/decisions.md#o31)).

## Accepted mechanism

Accepted 2026-10-04 on the owner's sign-off of [plan 17](../plans/17-scoped-permissions.md) Increment 0 (PR #60). The sections
below are the mechanism as signed off, with the owner's nine answers folded in and listed at the end. [plan 02](../plans/02-login-person-link.md)
(login to person) and [plan 10](../plans/10-route-points-convoy-leader.md) (`dbo.ConvoyLeaderAssignment`, `IConvoyLeaderRepository`) are
the inputs, and both are merged.

### 1. The roles

- **`ConvoyLeader` is derived, not issued.** The owner decided that a **Dispatcher or an Administrator marks a driver as Convoy
  Leader** (`PUT /convoys/{id}/leader`, `convoys:lead-assign`, already Administrator and Dispatcher). A Dispatcher cannot grant a role in
  the identity provider, so the identity provider is not involved: a linked login holding an **open** `ConvoyLeaderAssignment` is given
  the `ConvoyLeader` role **for that request** by an `IClaimsTransformation`, which adds a `roles` claim and nothing else. The role
  therefore grants nothing without a current assignment because it does not exist without one, and a reassignment takes effect on
  the next request. Nothing is stored in the token and nothing is cached beyond the request. A login that holds `GroundOfficer` is
  never given it (the isolation of that role runs both ways), and an unlinked login is never given it.
- The role is still a coarse capability: policies list it like any other role, and **every route that accepts it also carries the
  per-convoy scope** (section 3), so holding it for convoy 12 says nothing about convoy 13.
- **`Loader`** is unchanged as a role issued by the identity provider. The scope is new.
- **No new database role.** Both assignment tables live in `dbo`, read by `freedom_app`. Nothing here touches the `sensitive` schema.

### 2. The assignment sources

- **Leaders:** `dbo.ConvoyLeaderAssignment` as plan 10 built it. Current means `Until IS NULL`; the filtered unique index already
  guarantees one per convoy. A closed row never authorises anything. **Leader scope ends when the convoy closes** (`ClosedAt`,
  [plan 14](../plans/14-outcomes-closing.md)): the "current leader" question gains `AND convoy not closed` when that column exists,
  in `IConvoyLeaderRepository.IsCurrentLeaderAsync` and the led-convoys query, which is the one place both the claim and the scope read.
  **Revocation hook for [plan 19](../plans/19-leader-address-access.md):** a leader's access to destination addresses is revoked at that
  same moment, and plan 19 must read the leader's access through this same question (never its own copy) so closing the convoy,
  reassigning the leader and the end of the X11 window all end it in one place. Until plan 14 merges there is no `ClosedAt`, so the
  clause is a documented gap, not code.
- **Loaders:** a new `dbo.LoaderLocationAssignment`:

  | Column | Notes |
  |---|---|
  | `Id` | `int IDENTITY` |
  | `PersonId` | FK to `dbo.Person`, indexed (every erasure scans it) |
  | `LocationId` | FK to `dbo.Location`, `ON DELETE CASCADE` (as the leader row does for a convoy) |
  | `From`, `Until` | `datetime2(0)`; `Until` null while open |
  | `LastChangedBy`, `LastChangedAt` | the [ADR 0017](0017-every-entity-records-its-last-change.md) pair |

  A filtered unique index on `(PersonId, LocationId) WHERE Until IS NULL` makes "assigned" a single fact. **History is kept**
  (removal sets `Until`, not a delete), as the owner chose.
- **Who writes:** the Administrator only ([O31](../domain/decisions.md#o31)) through `GET /locations/{id}/loaders`,
  `PUT /locations/{id}/loaders/{personId}` and `DELETE /locations/{id}/loaders/{personId}`, under `locations:write`. Assignment is by
  person, so it can be made before the login is linked; an unlinked login simply cannot use it.
- **Erased people:** erasure already stamps `ErasedAt` when any record names the person, and an erased person has no `IdentitySubject`,
  so they can never resolve and their assignments are dead rows.

### 3. The handler: one `IAuthorizationHandler`, failing closed

A role cannot name a resource, and `RequireAuthorization(policy)` runs before the resource is known. So scoping is
**resource-based authorization**, invoked by an endpoint filter, in addition to the role policy, never instead of it.

- **Resources.** `ConvoyScope(int ConvoyId)` and `LocationScope(int? LocationId, bool AllowUnlocated)` implement a marker
  `IScopedResource`. `ScopedRequirement` carries the kind and the roles that are **exempt** from scope for that route.
- **One handler**, `ScopedAuthorizationHandler : AuthorizationHandler<ScopedRequirement, IScopedResource>`. It never calls `Fail()`
  and never succeeds by default: it calls `Succeed` only when one of the rules below holds, so anything not covered (an unknown
  resource kind, an unresolved caller, a null where a value is needed) is a denial. A failing assignment store propagates as a 500,
  never as success.
- **The rules, in order:**
  1. A caller who is not authenticated is denied.
  2. A caller holding an **exempt role** for the route succeeds. The roles are unioned (section 5), and exempt callers are never
     asked to be linked, so reads by an unlinked Administrator behave as they do today.
  3. Otherwise `ICurrentPerson.ResolveAsync` must return `Linked`. `NotLinked` is denied.
  4. `ConvoyScope`: a caller with `ConvoyLeader` succeeds only if `IConvoyLeaderRepository.IsCurrentLeaderAsync(convoyId, personId)`.
  5. `LocationScope`: a caller with `Loader` succeeds only if the location is assigned and open, or the location is null and
     the resource says `AllowUnlocated`.
  6. Otherwise denied. A role that is not the scoped one does not help: a `Loader` is not rescued by being a `ConvoyLeader`.
- **The port.** `IScopeAssignments` (Application/Abstractions) with `IsCurrentLeaderAsync`, `ManagesLocationAsync`,
  `ManagedLocationIdsAsync(personId)` and `LedConvoyIdsAsync(personId)`, composed from `IConvoyLeaderRepository` and the new
  `ILoaderAssignmentRepository`. Read on **every request**, never from a claim and never cached beyond the request.
- **Applying it.** `IScopeGuard` (Api) wraps `IAuthorizationService.AuthorizeAsync` for the current user and offers
  `CanAccessConvoyAsync`, `CanAccessLocationAsync` and `LocationVisibilityAsync`. Endpoint filters (`RequireConvoyScope`,
  `RequireBoxScope`, `RequireLocationScope`) call it from the route; the few body-dependent checks (the destination location of a box
  create or move) call it inline. A denial is `403` (problem type `out-of-scope`), the same for a Loader and a leader. **One
  exception:** `GET /boxes/scan/{token}` answers `404` for both an unknown and an out-of-scope token, so a QR token is not an oracle.
- **Lists are filtered in the query, not after it.** The query record takes an explicit `LocationVisibility` (all, or a set of
  location ids with an unlocated flag, where an empty set and false mean nothing), with **no default**, so a new list handler cannot
  compile without deciding. `ListBoxesQuery`, `ListLocationsQuery` push it into SQL.
- **Declared, or the build fails.** A component test walks the endpoint map: every endpoint whose policy is `boxes:*` or
  `locations:*`, or that is reachable by a `ConvoyLeader`, must carry scope metadata (`ScopedEndpoint`) or an explicit
  `ScopeExempt("reason")`, and the exempt list is pinned. A new route cannot silently skip scope.
- **Observability.** A `freedom.authz.scope` counter on a BCL `Meter` with bounded tags only (kind: convoy/location; outcome:
  allowed/denied; reason: exempt-role/not-linked/no-assignment). Never a person, convoy, location or box id as a tag.

### 4. The routes

**Convoy Leader, on their own convoy only.** The write routes arrive with [plan 14](../plans/14-outcomes-closing.md) and
[plan 18](../plans/18-leader-checklist-progress.md); neither has merged, so this branch builds the mechanism and applies it to the
reads below, and each of those plans declares the requirement on its routes (`RequireConvoyScope` plus a policy that lists
`ConvoyLeader` beside Administrator and Dispatcher, who keep them, [O27](../domain/decisions.md#o27)):

| Route | Plan that creates it |
|---|---|
| `POST /convoys/{id}/boxes/{boxId}/outcome` | 14 |
| `POST /convoys/{id}/vehicles/{vin}/declarations/goods-list/{receiverRef}/accepted` | 14 |
| `POST /convoys/{id}/vehicles/{vin}/delivered` | 14 |
| route marks, border crossings and fuel entries | 18 |
| address reads | 19, under its own narrower policy ([X12](../domain/decisions.md#x12)); not decided here |

**Leader reads (built here), each scoped to the led convoy** under the policy `convoys:read-led` (Administrator, Purchaser,
Dispatcher, Loader, ConvoyLeader; scope-exempt for every role but `ConvoyLeader`): `GET /convoys/{id}`, `GET /convoys/{id}/route`,
`GET /convoys/{id}/vehicles`, `GET /convoys/{id}/vehicles/{vin}/boxes`. Deliberately **not**: `GET /convoys` (the list, which would
leak every convoy), crew or people routes, receivers, declarations, budget, readiness. A leader finds their convoy from `GET /me`, which
gains `ledConvoyIds` and `managedLocationIds`, derived from `IScopeAssignments`, never from the token.

**Box ids reach scoped callers only through the convoy and manifest routes.** The owner decided that boxes appear on the manifest and
convoy routes and nowhere else, and that those routes are **not** location-scoped. `GET /convoys/{id}/vehicles/{vin}/boxes` is the one
box listing a leader can reach (their own convoy), and `/manifests/{id}/boxes|weight` is unchanged. The donor and donation routes
(`GET /donations`, `GET /donors/{id}/donations`, `GET /donors/{id}/report`) carry **no box id** today (their read models have nowhere to
hold one), and a test pins that, so a Loader cannot learn box ids, or a hub, through a donor.

**Loader, scoped to assigned locations.** *read* = box at an assigned location, or **unlocated**; *write* = box at an assigned
location; *move* = the source is assigned (or unlocated, which is the check-in) and the destination is assigned.

| Route | Scope applied |
|---|---|
| `GET /boxes` | filtered list: assigned locations, plus unlocated boxes |
| `GET /boxes/{id}` , `/items`, `/qr-code`, `/qr-code/image`, `/label`, `/bay`, `/bay/history` | read |
| `GET /boxes/scan/{token}` | read, 404 when out of scope |
| `POST /boxes` | the body `locationId` must be assigned, or null (an expected box, [O22](../domain/decisions.md#o22)) |
| `PUT /boxes/{id}` | move (this is the check-in of an unlocated box); a null destination is refused for a scoped Loader |
| `DELETE /boxes/{id}`, `POST|DELETE /boxes/{id}/items...`, `POST /boxes/{id}/validate`, `POST|DELETE /boxes/{id}/qr-code` | write |
| `PUT|DELETE /boxes/{id}/bay` | write on the box (a bay must already belong to the box's own location, which the handler enforces) |
| `PUT|DELETE /convoys/{id}/vehicles/{vin}/boxes/{boxId}` (allocation) | write on the box |
| `GET /locations`, `GET /locations/{id}`, `GET /locations/{id}/bays` | filtered list / `LocationScope` |
| `POST|PUT|DELETE /locations...`, `GET|PUT|DELETE /locations/{id}/loaders...` | Administrator only |

A Loader with **no assignment sees no box with a location and no location**: scope is allow-listed, so the empty set is nothing.
**Unlocated boxes** (expected, not yet at a hub) are readable by any Loader, who needs to find one to check it in. That reveals no hub
stock. They are **not** writable except by the move that checks them in.

### 5. The Administrator and the other roles

- **The Administrator bypasses scope**, as now. They assign the scope and hold every global permission already. The bypass is a
  role in `ScopedRequirement`, visible and tested.
- **Roles are unioned.** A login holds the capabilities of every role it has. Only `Loader` (and the derived `ConvoyLeader`) is
  narrowed by scope: a login that also holds `Dispatcher`, `Purchaser` or `Administrator` is exempt on routes those roles already
  reach. The seeded `operator` (Dispatcher, Loader, Mechanic, Purchaser) is therefore **not** scoped, as today. A scoped Loader must be
  a Loader-only login, so the realm gains a seeded `loader` (Loader only) and a seeded `leader` (no roles at all: they are a linked
  driver who is nominated).
- A scope-bearing role does not widen anything else: `ConvoyLeader` appears in `convoys:read-led` only.

### 6. Tests (how a caller is expressed)

- **Unit** (the handler, with substituted `ICurrentPerson` and assignment ports and a hand-built `ClaimsPrincipal`): role without
  assignment denied; assignment to another convoy or location denied; current allowed; closed (historical) denied; unlinked denied;
  Administrator allowed; an unlocated box is allowed only where the resource says so; a store exception is not a success; a `Loader`
  who is also a `ConvoyLeader` is not rescued across kinds. The claims transformation adds the role only for a linked login with an
  open assignment and never to a Ground Officer.
- **Component.** The test caller is already a linked volunteer (`InMemoryPersonRepository.TestUserId`); a test names that person in the
  in-memory leader or loader-assignment stores, and the fakes read the **same** stores the handlers write, so a fake enforces what the
  SQL does (open row only, `Until` honoured). The endpoint-map test of section 3 supplies the enumeration, so list and search routes
  cannot be missed.
- **Integration:** the two stores against real SQL, including the filtered unique indexes and the history rows.
- **BDD logins:** `leader` (linked and nominated by the scenario) and `loader` (Loader only, assigned to one location), because
  `operator` is unscoped by design.

### 7. What this does not do

- It does not decide address access: plan 19 adds its own narrower policy on top of this handler ([X12](../domain/decisions.md#x12)).
- It does not put assignments in tokens, create per-convoy roles or move the decision into handlers (the alternatives above stand).
- It changes no existing policy's roles. `web/src/auth/policyMatrix.ts` gains `convoys:read-led`, `ConvoyLeader` as a derived role,
  and nothing else; the web hides out-of-scope actions, and the server is the control.

### Owner's answers at sign-off

1. A Dispatcher **or** an Administrator marks a driver as Convoy Leader. Recorded in section 1: marking is the nomination, and the role is derived from it.
2. `dbo.LoaderLocationAssignment` with `From` and `Until` history.
3. Leader reads are `GET /convoys/{id}`, `/route`, `/vehicles` and `/vehicles/{vin}/boxes`; no convoy list; `/me` gains `ledConvoyIds` and `managedLocationIds`.
4. Leader scope ends at convoy closing, and address access is revoked then (section 2, with the hook for plan 19).
5. Any Loader may read an unlocated box; the only write is the check-in move.
6. Box ids are exposed only on the convoy and manifest routes, which are not location-scoped (section 4).
7. Roles union; only Loader is narrowed; the seeded `operator` is unscoped; a Loader-only seed login is added.
8. The Administrator bypasses scope.
9. `403` everywhere except `GET /boxes/scan/{token}`, which is `404`.

### Residual risks

- **Time of check, time of use.** A box could move location between the lookup and the write. A Loader can only move a box they could
  already write, so the worst case is acting on a box that was theirs a moment ago.
- **A forgotten route** is the dominant risk; the endpoint-map test and the no-default `LocationVisibility` exist for it.
- **The derived role costs one lookup per authenticated, linked request.** It is a single indexed `EXISTS` on a table with one open row per convoy.

# 10. Permissions can be scoped to a resource: a Convoy Leader to one convoy, a Loader to their locations

Date: 2026-10-02

## Status

**Proposed.** The need is decided ([X12](../domain/decisions.md#x12), [O14](../domain/decisions.md#o14)). The
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

- **The mechanism stays Proposed until a decision checkpoint.** The first step of
  [plan 17](../plans/17-scoped-permissions.md) writes the concrete mechanism into this ADR, opens a draft PR and stops
  for the owner's sign-off. This ADR becomes Accepted then, not before.
- **A login is linked to a person** by storing the identity provider's subject on the erasable person details,
  linked by an Administrator ([decision O34](../domain/decisions.md#o34)). That resolves the first consequence above,
  and is built in [plan 02](../plans/02-login-person-link.md).
- **An unlinked login is refused (403) on any write that records who did it** ([decision O35](../domain/decisions.md#o35)).
- **The Administrator assigns Loaders to locations** ([decision O31](../domain/decisions.md#o31)).

## Proposed mechanism (awaiting owner sign-off)

Added 2026-10-04 by [plan 17](../plans/17-scoped-permissions.md) Increment 0. **Nothing here is built, and this ADR stays
Proposed until the owner signs off in the PR.** Items marked **Owner decision** are the ones the sign-off must settle;
each carries a recommendation. [plan 02](../plans/02-login-person-link.md) (login to person) and
[plan 10](../plans/10-route-points-convoy-leader.md) (`dbo.ConvoyLeaderAssignment`, `IConvoyLeaderRepository`) are merged,
so both inputs exist.

### 1. The roles

- **`ConvoyLeader`** is a new application role in the flat `roles` claim, delivered by a Keycloak group of the same name
  locally and an Entra app role in Azure. **It grants nothing by itself**: every route that accepts it also needs a current
  assignment (section 3). A `ConvoyLeader` with no open `ConvoyLeaderAssignment` is refused everywhere it would otherwise apply.
- **Who holds it. Owner decision.** Recommended: **only drivers an Administrator marks**, by adding the login to the
  `ConvoyLeader` group or app role. Not every driver, because there is no `Driver` role today and a driver is a *person*
  (a flag on the record), not necessarily a login. Cost to accept: nominating a leader (`PUT /convoys/{id}/leader`, plan 10)
  does not grant the role, because the identity provider is a second system Freedom does not write to, so a nominated driver
  whose login lacks the role is simply refused. The runbook step is "grant the role, then nominate". Alternative: drop the
  role requirement and let the open assignment alone be the authority. That removes the two-system step but removes the
  second lock, and a login that was never meant to act would be able to the moment it is linked and nominated. Not recommended.
- **`Loader`** is unchanged as a role. The scope is new.
- **No new database role.** Both assignment tables live in `dbo`, read by `freedom_app`. Nothing here touches the `sensitive` schema.

### 2. The assignment sources

- **Leaders:** `dbo.ConvoyLeaderAssignment` as plan 10 built it. Current means `Until IS NULL`; the filtered unique index
  already guarantees one per convoy. A closed row never authorises anything.
- **Loaders:** a new `dbo.LoaderLocationAssignment`:

  | Column | Notes |
  |---|---|
  | `Id` | `int IDENTITY` |
  | `PersonId` | FK to `dbo.Person`, indexed (every erasure scans it) |
  | `LocationId` | FK to `dbo.Location`, `ON DELETE CASCADE` (as the leader row does for a convoy) |
  | `From`, `Until` | `datetime2(0)`; `Until` null while open |
  | `LastChangedBy`, `LastChangedAt` | the [ADR 0017](0017-every-entity-records-its-last-change.md) pair |

  A filtered unique index on `(PersonId, LocationId) WHERE Until IS NULL` makes "assigned" a single fact. **History is kept**
  (removal sets `Until`, not a delete), because this ADR already says who managed a location when is an audit fact.
  **Owner decision:** the plan names only `(PersonId, LocationId)`; recommended is the `From/Until` form above. A plain
  two-column table is simpler but loses that history.
- **Who writes:** the Administrator only ([O31](../domain/decisions.md#o31)) through `GET /locations/{id}/loaders`,
  `PUT /locations/{id}/loaders/{personId}` and `DELETE /locations/{id}/loaders/{personId}`, under `locations:write`.
  Assignment is by person, so it can be made before the login is linked; an unlinked login simply cannot use it.
- **Erased people:** erasure already stamps `ErasedAt` when any record names the person, and an erased person has no
  `IdentitySubject`, so they can never resolve and their assignments are dead rows.

### 3. The handler: one `IAuthorizationHandler`, failing closed

A role cannot name a resource, and `RequireAuthorization(policy)` runs before the resource is known. So scoping is
**resource-based authorization**, invoked by an endpoint filter, in addition to the role policy, never instead of it.

- **Resources.** `ConvoyScope(int ConvoyId)` and `LocationScope(int? LocationId)` implement a marker `IScopedResource`.
  `ScopedRequirement` carries the kind and the roles that are **exempt** from scope for that route.
- **One handler**, `ScopedAuthorizationHandler : AuthorizationHandler<ScopedRequirement, IScopedResource>`. It never calls
  `Fail()` explicitly and never succeeds by default: it calls `Succeed` only when one of the rules below holds, so anything
  not covered (an unknown resource kind, an unresolved caller, a store error, a null where a value is needed) is a denial.
  Exceptions from the assignment store propagate as 500, never as success.
- **The rules, in order:**
  1. The caller must be authenticated and `ICurrentPerson.ResolveAsync` must return `Linked`. `NotLinked` is denied.
  2. A caller holding an **exempt role** for the route succeeds (section 5).
  3. `ConvoyScope`: a caller with `ConvoyLeader` succeeds only if `IScopeAssignments.IsCurrentLeaderAsync(convoyId, personId)`.
  4. `LocationScope`: a caller with `Loader` succeeds only if `IScopeAssignments.ManagesLocationAsync(personId, locationId)`.
  5. Otherwise denied. A role that is not the scoped one does not help: a `Loader` is not rescued by being a `ConvoyLeader`.
- **The port.** `IScopeAssignments` (Application/Abstractions) with `IsCurrentLeaderAsync`, `ManagesLocationAsync` and
  `ManagedLocationIdsAsync(personId)`, backed by Dapper over the two tables. Assignments are read **on every request**,
  never from a claim and never cached beyond the request, so a reassignment takes effect on the next call (the property the
  token-claim alternative lacked).
- **Applying it.** An endpoint filter `ScopedTo.Convoy("id")` / `ScopedTo.Location(...)` builds the resource from the route and
  calls `IAuthorizationService.AuthorizeAsync`. For routes that name a **box**, a small port `IBoxLocationLookup` returns the box
  `LocationId` first. A denial is `403` (problem type `out-of-scope`), the same for a Loader and a leader. Box and convoy
  identifiers are sequential integers, so hiding existence behind a 404 buys nothing. **One exception:** `GET /boxes/scan/{token}`
  answers `404` for both an unknown and an out-of-scope token, so a QR token is not an oracle.
- **Lists are filtered in the query, not after it.** The query record takes an explicit `Visibility` (all, or a set of location
  ids, where an empty set means nothing), with **no default**, so a new list handler cannot compile without deciding.
  `ListBoxesQuery`, the location list and any filter push it into SQL (`WHERE LocationId IN @ids OR LocationId IS NULL`).
- **Declared, or the build fails.** A component test walks the endpoint map: every endpoint whose policy is `boxes:*` or
  `locations:*` must carry `ScopedTo` metadata or an explicit `ScopeExempt("reason")`, and the exempt list is pinned. A new route
  cannot silently skip scope.
- **Observability.** A `freedom.authz.scope` counter with bounded tags only (kind: convoy/location; outcome: allowed/denied;
  reason: not-linked/no-assignment/exempt-role). Never a person, convoy, location or box id as a tag, span attribute or log field.

### 4. The routes

**Convoy Leader, on their own convoy only** (policy `convoys:leader-act` = Administrator, Dispatcher, ConvoyLeader, plus
`ScopedTo.Convoy("id")`; the Dispatcher keeps these routes as now, [O27](../domain/decisions.md#o27) for marks):

| Route | Plan that creates it |
|---|---|
| `POST /convoys/{id}/boxes/{boxId}/outcome` | 14 |
| `POST /convoys/{id}/vehicles/{vin}/declarations/goods-list/{receiverRef}/accepted` | 14 |
| `POST /convoys/{id}/vehicles/{vin}/delivered` | 14 |
| route marks, border crossings and fuel entries | 18 (each declares the requirement) |
| address reads | 19, under their own narrower policy ([X12](../domain/decisions.md#x12)); not decided here |

**Leader reads (Owner decision on the minimum).** A leader needs to see what they are accounting for. Recommended, each scoped
to the led convoy: `GET /convoys/{id}`, `GET /convoys/{id}/route`, `GET /convoys/{id}/vehicles`,
`GET /convoys/{id}/vehicles/{vin}/boxes`. Deliberately **not**: `GET /convoys` (the list, which would leak every convoy),
crew or people routes, receivers, declarations, budget, readiness. The leader finds their convoy from `GET /me`, which gains
`ledConvoyIds` (and `managedLocationIds` for a Loader), derived from `IScopeAssignments`, never from the token.

**Leader scope ends when the convoy closes (Owner decision).** An open assignment stays open after arrival (nomination is
refused once arrived, but nothing closes the row), and outcomes and acceptance are recorded after arrival. Recommended: the
handler also denies once `dbo.Convoy.ClosedAt` is set (plan 14). Until plan 14 merges that clause has nothing to read, and the
Increment that adds it is plan 14 or 18, whichever lands second. The window for address reads (X11) is plan 19's, not this one.

**Loader, scoped to assigned locations.** Rule names: *read* = box at an assigned location, or **unlocated**; *write* = box at an
assigned location; *move* = source and destination both assigned (an unlocated source counts as the check-in).

| Route | Scope applied |
|---|---|
| `GET /boxes` | filtered list: assigned locations, plus unlocated boxes |
| `GET /boxes/{id}` , `/items`, `/qr-code`, `/qr-code/image`, `/label`, `/bay`, `/bay/history` | read |
| `GET /boxes/scan/{token}` | read, 404 when out of scope |
| `POST /boxes` | the body `locationId` must be assigned, or null (an expected box, [O22](../domain/decisions.md#o22)) |
| `PUT /boxes/{id}` | move (this is the check-in of an unlocated box) |
| `DELETE /boxes/{id}`, `POST|DELETE /boxes/{id}/items...`, `POST /boxes/{id}/validate`, `POST|DELETE /boxes/{id}/qr-code` | write |
| `PUT|DELETE /boxes/{id}/bay` | write on the box, and the bay must belong to an assigned location |
| `PUT|DELETE /convoys/{id}/vehicles/{vin}/boxes/{boxId}` (allocation) | write on the box |
| `GET /locations`, `GET /locations/{id}`, `GET /locations/{id}/bays` | filtered list / `LocationScope` |
| `POST|PUT|DELETE /locations...`, `GET|PUT|DELETE /locations/{id}/loaders...` | unchanged: Administrator only |

A Loader with **no assignment sees no box with a location and no location**: scope is allow-listed, so the empty set is
nothing. **Unlocated boxes** (expected, not yet at a hub) are readable by any Loader, who needs to find one to check it in.
That reveals no hub stock. They are **not** writable except by the move that checks them in. **Owner decision:** the alternative is
that a Loader never sees an unlocated box and a Dispatcher checks every one in, which is stricter but breaks the hub scan flow.

**Routes that return box data but are not location-scoped, listed so the omission is deliberate (Owner decision):**
`GET /convoys/{id}/vehicles/{vin}/boxes`, `GET /manifests/{id}/boxes`, `GET /manifests/{id}/weight` (cargo on a truck is convoy
data, readable under the existing `convoys:read` and `manifests:read`, and a filtered list would give a wrong weight);
`GET /donations`, `GET /donors/{id}/donations`, `GET /donors/{id}/report` (donor data, not located). These carry box identifiers and
weights, never a hub name. If the owner wants a Loader kept out of them, say so, and the increment adds the filter.

### 5. The Administrator and the other roles

- **The Administrator bypasses scope**, as now (recommended: yes). They assign the scope and hold every global permission already,
  so scoping them would add a failure mode for no gain. The bypass is a role in `ScopedRequirement`, visible and tested.
- **Roles are unioned, so a login holding a scope-free role for the route is not scoped.** The seeded `operator` is Dispatcher
  and Loader and Purchaser: for `boxes:read` it already holds roles O14 does not narrow, so it sees everything, as today. O14
  narrows a Loader, not a Dispatcher. **Owner decision:** confirm that is the intent. The consequence is that a person who
  is both a Dispatcher and a Loader is never scoped, and a scoped Loader must be a Loader-only login.
- A scope-bearing role does not widen anything else: `ConvoyLeader` appears in no existing policy, and `Loader`'s existing
  policies are unchanged apart from the scope above.

### 6. Tests (how a caller is expressed)

- **Unit** (the handler, with a substituted `ICurrentPerson` and `IScopeAssignments` and a hand-built `ClaimsPrincipal`): role without
  assignment denied; assignment to another convoy or location denied; current allowed; closed (historical) denied; unlinked denied;
  Administrator allowed; a store exception is not a success; a `Loader` who is also a `ConvoyLeader` is not rescued across kinds.
- **Component.** `TestAuthOptions` gains `Subject` (default `test-user`, as now) and the existing roster used for
  `FindBySubjectAsync` maps it to a `PersonId`. A `TestCaller` builder sets roles, person and assignments in one place
  (`FreedomApi.WithCaller(TestCaller.Loader(managing: [1]))`, `TestCaller.Leader(of: 12)`), and an in-memory `IScopeAssignments`
  reads the **same** in-memory leader and loader-assignment stores the fakes write, so a fake enforces what the SQL does
  (open row only, `Until` honoured). The endpoint-map test of section 3 supplies the enumeration: for each declared `ScopedTo` route
  one test drives an in-scope and an out-of-scope caller, so the list and search routes cannot be missed.
- **Integration:** the two stores against real SQL, including the filtered unique indexes and the history rows.
- **BDD logins:** `leader` (ConvoyLeader, linked and nominated by the scenario) and a new `loader` (Loader only, assigned to one
  location), because `operator` is unscoped by design (section 5). Seeds are in plan 17 Increment 5/6.

### 7. What this does not do

- It does not decide address access: plan 19 adds its own narrower policy on top of this handler ([X12](../domain/decisions.md#x12)).
- It does not put assignments in tokens, create per-convoy roles or move the decision into handlers (the alternatives above stand).
- It changes no existing policy's roles. `web/src/auth/policyMatrix.ts` gains only `convoys:leader-act` and the `ConvoyLeader` role;
  the web hides out-of-scope actions, and the server is the control.

### Residual risks to review

- **Time of check, time of use.** A box could move location between the lookup and the write. A Loader can only move a box
  they could already write, so the worst case is acting on a box that was theirs a moment ago. The write statements for box moves should
  carry the location in the `WHERE` where it is cheap.
- **A forgotten route** is the dominant risk; the endpoint-map test and the no-default `Visibility` exist for it.
- **The role/assignment split across two systems** (section 1) fails closed: it refuses a nominated leader, never admits one.

# 17. Resource-scoped permissions: the Convoy Leader and the Loader

| | |
|---|---|
| **Branch** | `feat/scoped-permissions` |
| **Covers** | [ADR 0010](../adr/0010-resource-scoped-permissions.md); [X12](../domain/decisions.md#x12), [O14](../domain/decisions.md#o14), [O31](../domain/decisions.md#o31), [D17](../domain/decisions.md#d17), [P14](../domain/decisions.md#p14) |
| **Depends on** | [02](02-login-person-link.md), [10](10-route-points-convoy-leader.md) |
| **Gate** | **Increment 0: the owner signs off the mechanism.** Stop. |

## Context

Every permission is global today. `AddFreedomAuthorization` (`src/UA.Action.Freedom.Api/Configuration/
AuthenticationExtensions.cs`) defines each policy as a set of roles from a flat `roles` claim
(`MapInboundClaims = false`). The roles are Administrator, Purchaser, Dispatcher, Loader, Mechanic and GroundOfficer.
**There is no `ConvoyLeader` or `Driver` role.** The Keycloak realm (`iac/tofu/keycloak.tf`, `local.app_roles`
l.82–89) grants roles through groups. `web/src/auth/policyMatrix.ts` mirrors the server, pinned by a test.

Two decisions need "this person, on this thing":
- **the Convoy Leader acts on their own convoy only** ([X12](../domain/decisions.md#x12));
- **a Loader sees only the locations they manage** ([O14](../domain/decisions.md#o14)).

[Plan 02](02-login-person-link.md) links logins to people, and [plan 10](10-route-points-convoy-leader.md) records who
leads each convoy.

ADR 0010 is **Proposed**: the owner chose to approve the mechanism before any code.

## Increment 0: the gate

**Write the concrete mechanism into ADR 0010, commit, open a draft PR labelled `awaiting-owner`, and stop.** It must
state:
1. **The role:** a new `ConvoyLeader` app role, granted to drivers who may lead. It grants **nothing** without a current
   assignment. Say whether every driver gets it, or only those an Administrator marks.
2. **The assignment sources:** `dbo.ConvoyLeaderAssignment` (from plan 10) for leaders, and a new
   `dbo.LoaderLocationAssignment (PersonId, LocationId)` maintained by the Administrator ([O31](../domain/decisions.md#o31)).
3. **The handler:** one `IAuthorizationHandler` for a resource requirement (`ConvoyScope(convoyId)` and
   `LocationScope(locationId)`), resolving the caller through `ICurrentPerson` and **failing closed**.
4. **The endpoint list:** every route that will accept a leader on their own convoy (outcomes, acceptance, marks, fuel,
   and later address reads), and every Loader route that becomes location-scoped, **including list and search
   routes**.
5. **The Administrator:** whether they bypass scope (recommended: yes, as now).
6. **Tests:** how `TestAuthHandler` (`tests/UA.Action.Freedom.Tests.Component/TestAuth.cs`) expresses a caller's person
   and assignments.

**Resume only when the owner has signed off.** Then change ADR 0010's status to Accepted, in this branch.

## Increments

### Increment 1: the handler fails closed

- **Api:** a `ScopedRequirement` and handler.
- **Tests (RED first):** unit tests:
  - the role without an assignment is denied;
  - an assignment to another convoy is denied;
  - the current assignment is allowed;
  - a closed (historical) assignment is denied;
  - an unlinked login is denied;
  - an Administrator is allowed (if the gate says so).

### Increment 2: Loader to location

- **Schema:** `dbo.LoaderLocationAssignment`, with audit columns.
- **Api:** `GET|PUT|DELETE /locations/{id}/loaders/{personId}`, Administrator only.
- **Tests:** integration and component tests.

### Increment 3: Loader scoping, reads included

- Every Loader-reachable route that names a location or a box at a location is scoped:
  - box create and update, items, validate, QR, bay assignment and bay history;
  - location and bay reads.
- **List and search** (`GET /boxes`, `GET /locations`, and any filter) return only the Loader's locations.
- **Tests:** component tests that a Loader cannot read, list, find or act on another location's boxes **through any
  route**. Enumerate the routes from the endpoint map.

### Increment 4: the Convoy Leader on their own convoy

- Add the `ConvoyLeader` role to the routes that [plan 14](14-outcomes-closing.md) gave the Dispatcher on the leader's
  behalf:
  - box outcomes;
  - acceptance per goods list;
  - vehicle delivered;
  - and, once [plan 18](18-leader-checklist-progress.md) adds them, marks and fuel.
- The handler restricts them to the leader's current convoy.
- **Tests:** component tests: the leader is allowed on their own convoy, denied on another, and denied after
  reassignment.

### Increment 5: identities, policies and the web

- `iac/tofu/keycloak.tf`: a `ConvoyLeader` role and group, and a seed user `leader` (password `password`) in it.
- `web/src/auth/roles.ts` and `policyMatrix.ts`, with its pinning test.
- Change `database/.../Security/Roles.sql` only if a database role is needed (it should not be).
- An Administrator UI for assigning Loaders to locations.
- The web hides out-of-scope actions, but the **server is the control**.
- Tests: Vitest with MSW.

### Increment 6: BDD

- New logins in the BDD support: `leader` and a scoped Loader.
- `Features/ScopedPermissions.feature`:
  - the leader records an outcome on their convoy;
  - the leader is refused on another convoy;
  - the Loader cannot see a box at another location.

## Retires and transitional

- **Retires:** the Dispatcher-only workaround for leader actions. The Dispatcher keeps the ability
  ([O27](../domain/decisions.md#o27) for marks), and the leader gains it.
- **Not here:** address reads are [plan 19](19-leader-address-access.md).

## Docs to update

- `CLAUDE.md`: the Auth section (scoped requirements, the `ConvoyLeader` role, Loader scope) and the policy list.
- `README.md`: the policy matrix.
- `docs/local-authentication.md`: the new login, the role and the matrix.
- `docs/domain/key-concepts.md` § Roles.
- ADR 0010: status Accepted, and an implementation note.
- Remove the row from the decisions amendments table.

## Risks

- **Lists leak, single reads do not.** Most scoping bugs are a list or search route that forgot the filter. Increment 3
  must enumerate every route.
- **The Keycloak subject for `leader`** must be linked to a person by the [plan 02](02-login-person-link.md) BDD hook.
- Run `/security-review` before opening the PR.

## Testing

The handler (Unit), assignments (Integration), every scoped route including lists (Component), Keycloak, policy matrix
and web, and BDD with the new logins.

## Verification

The standard gates, plus on the local stack: as `leader` assigned to convoy A, record an outcome on A (allowed) and on
B (403). Reassign the leader, and A is refused too. As a Loader assigned to location X, `GET /boxes` lists only X.

## Sequencing

After plans [02](02-login-person-link.md) and [10](10-route-points-convoy-leader.md). Needed by
[plan 18](18-leader-checklist-progress.md) and [plan 19](19-leader-address-access.md).

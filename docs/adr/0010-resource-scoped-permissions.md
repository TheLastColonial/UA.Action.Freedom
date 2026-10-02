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

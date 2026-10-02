# 17. Every entity records who last changed it, and when

Date: 2026-10-02

## Status

Accepted. Not yet implemented.

## Context

Freedom already records *who did the one thing that matters* in several places, and nothing else: `Box.ValidatedBy`
and `ValidatedAt`, `Convoy.TruckListPublishedAt`, the insurer's `RecordedBy` (taken from the login, never typed in),
`BoxBayAssignment` (who, when) and the receiver-detail audit row. Each was added when a feature needed it. An
entity that is simply *edited*, a convoy's dates, a vehicle's mileage, a route, an item's description, carries no
trace of who edited it.

That is about to matter more. The Convoy Leader changes box statuses on the road
([ADR 0015](0015-box-and-vehicle-outcomes-and-convoy-closing.md)), a Dispatcher and an Administrator can both
change a load and re-approve it ([ADR 0004](0004-the-manifest-is-the-load-sign-off.md)), and the project owner
asked that the system **capture who last made changes to different entities**
([decision O19](../domain/decisions.md#o19)). When a number is wrong, the first question is who changed it and
when.

## Decision

**Every entity records the person who last changed it and the time it was changed.** The person is taken **from
the login, never typed in**, as for insurance. It is the identity of whoever made the request, as resolved at the
edge, and not a value a caller can supply.

- It is **not a history**. It answers *who changed this last*, not *what it was before*.
- It is **set by every update statement**, in the same statement as the change, so it cannot lag the change.
- It does **not replace** the purpose-built records above. A validation stamp, a bay assignment and a receiver-detail
  audit row each say something richer, and stay.
- A reference to a person who has been **erased** reads as it does for volunteers: **"Former volunteer"**, an
  anonymous identity with nothing linking it back
  ([key concepts](../domain/key-concepts.md#volunteer-erasure)).

## Alternatives considered

**Temporal tables or a full audit log.** They answer *what was it before*, and cost storage in a design bound to
the Azure SQL free offer, and they hold personal data in a place erasure has to reach. The need stated was *who
last changed it*, and a column answers that.

**Application-level logging only.** Logs are not the system of record, are not queryable by the people who need
the answer, and by rule must not hold person-identifying values.

**Add it entity by entity when a feature asks.** The current approach, and the reason there is no answer today for
most entities.

## Consequences

**Every table gains two columns,** and every `UPDATE` in every Dapper repository sets them. Where the statement is
a conditional, write-once stamp (`WHERE … AND TruckListPublishedAt IS NULL`), it sets them too, and the stamp's
own semantics are unchanged.

**The in-memory repositories must set them.** The Component tests' `InMemory*Repository` fakes must behave as the
SQL does, which is the standing rule for fakes in this project, or tests will pass against behaviour production
does not have.

**The caller's identity has to reach the repository.** Handlers currently receive requests and not callers. How the
person flows from the authenticated principal to the statement, without becoming a client-supplied field, is a
design question for the first slice that needs it ([ADR 0010](0010-resource-scoped-permissions.md) needs the same
mapping from a login to a person).

**It cannot answer *what changed*.** That is accepted. Where it is genuinely needed, as it was for a validated box,
a purpose-built record is added, as it was then.

**Retention is open.** How long this is kept, and how it sits with erasure, is part of
[Q-retention](../domain/decisions.md#q-retention).

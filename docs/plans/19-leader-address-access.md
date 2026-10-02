# 19. The Convoy Leader reads destination addresses

| | |
|---|---|
| **Branch** | `feat/leader-address-access` |
| **Covers** | [ADR 0009](../adr/0009-convoy-leader-reads-destination-addresses.md); [X7](../domain/decisions.md#x7), [X9](../domain/decisions.md#x9), [X11](../domain/decisions.md#x11), [X12](../domain/decisions.md#x12), [X13](../domain/decisions.md#x13), [O1](../domain/decisions.md#o1), [O26](../domain/decisions.md#o26) |
| **Depends on** | [04](04-receiver-registration.md), [18](18-leader-checklist-progress.md) |
| **Gate** | **Increment 0: security review.** Stop for the owner and a security reviewer. |

## Context

A Ukrainian delivery address is the most sensitive data in the system. Today three independent controls keep it from
everyone but a Ground Officer:

1. the `receivers:detail` policy (Ground Officer only);
2. a separate database identity: `ISensitiveDbConnectionFactory` → `ConnectionStrings:FreedomSensitive`, used only by
   `ReceiverDetailRepository`;
3. `DENY SELECT ON SCHEMA::sensitive TO freedom_app` (`database/.../Security/Permissions.sql`).

`ReceiverDetailRepository.ResolveAsync` (l.22) writes `sensitive.ReceiverDetailAccessLog (Id, ReceiverRef,
PrincipalId, ReadAt, Reason)` in the same transaction as the read. `RedactingActivityProcessor`
(`src/UA.Action.Freedom.Telemetry/`) blanks SQL on the `sensitive` schema.

ADR 0009, widened by [O26](../domain/decisions.md#o26), lets the **Convoy Leader read the route and the addresses of
all Receivers on their convoy**, from **14 days before planned departure** until reassignment or arrival. Before then
they see headers only. Other drivers see nothing ([O1](../domain/decisions.md#o1)).

## Increment 0: the gate

**Write `docs/security/0009-review.md`, commit it, open a draft PR labelled `awaiting-owner`, and stop.** Cover:

1. **The threat model:**
   - a lost or stolen phone;
   - shoulder surfing;
   - a leader detained at a border with an open session;
   - a reassigned leader;
   - a leader who is also on a later convoy;
   - an insider Dispatcher who reassigns themselves.
2. **The wider scope ([O26](../domain/decisions.md#o26)):** every Receiver on the convoy, not only a final
   destination. Quantify it, for example with a typical convoy's Receiver count.
3. **Session controls:** the token and session lifetime on the checklist page, re-authentication before revealing
   addresses, an idle timeout, and sign-out on reassignment.
4. **Stores:** which reads come from `sensitive.ReceiverDetail`, and which from route points (`dbo.ConvoyRouteStop`).
   Confirm that route-point addresses for Ukrainian stops do not duplicate Receiver detail outside the `sensitive`
   schema. If they do, that is a finding.
5. **Audit:**
   - what a leader's read records: who, which convoy, which Receiver, in what capacity, and when;
   - how a Ground Officer or Administrator reviews it;
   - [Q-retention](../domain/decisions.md#q-retention) is still open.
6. **Database identity:** whether the leader path reuses the `ground_officer` database principal, or needs a narrower
   one that can read only through a view or stored procedure filtered by convoy. Recommend one.
7. **Tests that prove each control,** and what cannot be prevented: screenshots and browser history.

**Resume only after the owner and a security reviewer sign off.** Record any changes in ADR 0009.

## Increments

### Increment 1: the window, as a pure function

- **Domain:** `LeaderAddressAccess.IsOpen(plannedDeparture, asOf, windowDays, currentLeader, caller, convoyArrived)`.
- It is true only for the current leader, from `plannedDeparture - windowDays` until arrival.
- `windowDays` is configuration, default 14 ([X11](../domain/decisions.md#x11)).
- **Tests (RED first):** a unit table:
  - before the window;
  - inside it;
  - after arrival;
  - after reassignment;
  - for a non-leader;
  - when the planned departure moves (the window follows it).

### Increment 2: a narrower policy and the scoped read

- **Api:** a new policy, `receivers:convoy-addresses` (name per the review). **Not** an extension of
  `receivers:detail` ([X12](../domain/decisions.md#x12)). It requires the `ConvoyLeader` role plus the
  [plan 17](17-scoped-permissions.md) convoy scope, plus `IsOpen`.
- **Route:** `GET /convoys/{id}/addresses` returns the route points with their addresses, and every Receiver on the
  convoy (each box's and each vehicle's) with its address and contact, as the review allows.
- **Before the window:** headers only, never an address ([X13](../domain/decisions.md#x13)).
- **Tests:** component tests for:
  - the leader inside the window;
  - the leader outside it (headers only);
  - another convoy's leader (403);
  - a Ground Officer (their own policy is unchanged and still works);
  - a Dispatcher (403);
  - an unlinked login (403).

### Increment 3: through the sensitive path, audited

- `ReceiverDetailRepository.ResolveAsync` gains a **reader capacity** (`GroundOfficer | ConvoyLeader`) and a
  **convoy id**. The access log records both.
- **Schema:** add columns to `ReceiverDetailAccessLog`.
- The leader's read uses `ISensitiveDbConnectionFactory`, or the narrower principal the review chose.
  `freedom_app` stays denied.
- **Tests:**
  - Integration: the read and its audit row happen in **one transaction**.
  - The existing `tests/UA.Action.Freedom.Tests.Integration/Receivers/ReceiverSegregationTests.cs` still proves
    `freedom_app` cannot read the `sensitive` schema.
  - A new test for the leader principal's limits, if one was created.

### Increment 4: nothing printed, logged or queued

- Extend the redaction tests for `RedactingActivityProcessor` to the new route: no address in span attributes, logs or
  metrics tags.
- Assert the address never appears in a queue message, a manifest document request, the label or the filing sheet.
  Reuse the shape of the label absence tests.
- The route sends `Cache-Control: no-store`.
- **Tests:** component tests and telemetry tests.

### Increment 5: the checklist page shows addresses in the window

- The [plan 18](18-leader-checklist-progress.md) page fetches `/convoys/{id}/addresses` when the window is open,
  behind the re-authentication the review requires. It shows the addresses inline, with a "hide" control.
- It writes nothing to storage, and clears addresses from memory on sign-out and on navigation away.
- Tests: Vitest with MSW (window closed shows headers; open shows addresses; nothing in storage); a Playwright mobile
  test.

### Increment 6: BDD

- `Features/LeaderAddresses.feature`:
  - before the window, headers only;
  - inside it, addresses;
  - another convoy's leader is refused;
  - after reassignment, refused;
  - every read is audited and visible to a Ground Officer.

## Retires and transitional

- Nothing is retired. The Ground Officer's path is unchanged.

## Docs to update

- `CLAUDE.md`: the receiver segregation section, which now lists **four** readers and the controls on the fourth.
- `README.md`: endpoints and the policy matrix.
- `docs/local-authentication.md`.
- `docs/domain/key-concepts.md` § Data Sensitivity. Remove the row from the decisions amendments table.
- ADR 0009: status, the review outcome, and an implementation note.

## Risks

- **This is the highest-risk change in the series.** Do not shortcut the gate. Run `/security-review` before the PR,
  and attach its output.
- **Duplicate address storage.** If route points hold Ukrainian addresses outside the `sensitive` schema, the existing
  segregation has a hole independent of this plan. The review must say so.

## Testing

The window (Unit), the policy and scope (Component), the audit in one transaction and the database segregation
(Integration), redaction and absence (Component and telemetry), the page (Vitest and Playwright), and BDD.

## Verification

The standard gates, plus on the local stack:
1. Set a convoy's planned departure 20 days ahead. The leader sees headers only.
2. Move it to 10 days ahead. The leader sees addresses after re-authenticating.
3. Reassign the leader. The old leader gets 403.
4. As `groundofficer`, the access log shows each leader read with its capacity and convoy.
5. Grafana and Loki show no address.

## Sequencing

Last. After plans [04](04-receiver-registration.md) and [18](18-leader-checklist-progress.md). Increment 0 may start
earlier, in parallel, because a review takes time.

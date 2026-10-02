# 9. The Convoy Leader may read destination addresses, scoped, time-limited and audited

Date: 2026-10-02

## Status

Accepted. Not yet implemented. **Needs a security review before implementation**, because it widens the most
sensitive control in the system. Amends the receiver segregation described in
[ADR 0003](0003-ens-declaration-recorded-not-submitted.md) and `docs/domain/key-concepts.md` § Data Sensitivity.

## Context

A Ukrainian delivery address is the highest-risk data Freedom holds. A manifest listing precise addresses is a
targeting document, and it crosses several borders where it may be inspected or seized. Three independent controls
therefore keep it from everyone but a Ground Officer, and each holds if the others are removed by mistake:

1. the `receivers:detail` policy, Ground Officer alone;
2. a separate database identity, `ISensitiveDbConnectionFactory`, used only by `ReceiverDetailRepository`;
3. `DENY SELECT ON SCHEMA::sensitive TO freedom_app`, so the application's own identity cannot read an address.

The existing principle, stated in [Data Sensitivity](../domain/key-concepts.md#data-sensitivity), was that precise
delivery detail "is released to the driver at the point of delivery".

Product discovery found the convoy cannot be led that way. The **Convoy Leader** is the one driver responsible for
the convoy, in radio contact with every member, and **has to drive the convoy to the right place and arrive there
with it**. They need to understand the route and the final destination, and the project owner decided they should
see them: all route stops and the final destination, from when the delivery window approaches
([decisions X7 to X13](../domain/decisions.md#x7)). Other drivers are given **nothing**, and get their direction
from the leader by radio ([decision O1](../domain/decisions.md#o1)).

## Decision

**A Convoy Leader may read the route and the final destination of the convoy they lead.** It is a **fourth reader**
of address detail, and the widening is made as narrow as its purpose.

| Safeguard | Rule |
| --- | --- |
| **Scoped to their own convoy** | They read only the addresses of the convoy they currently lead, and only while they lead it. |
| **A new, narrower permission** | Not an extension of `receivers:detail`. The Ground Officer policy is unchanged, and a reviewer sees the new path ([decision X12](../domain/decisions.md#x12)). |
| **Audited, through the sensitive path** | Every read is audited in the same transaction as the read, and goes through `ReceiverDetailRepository` and `ISensitiveDbConnectionFactory`, so the database `DENY` stays in force for the application. |
| **Never printed or logged** | The address appears on the checklist page only. It stays off the manifest, label, filing sheet, telemetry and every queue message. |
| **Time-limited** | Access **opens 14 days before the planned departure**, and ends on reassignment or arrival ([X11](../domain/decisions.md#x11)). The figure is configuration. |
| **Headers before then** | Until the window opens they see each route point's name and kind, without details ([X13](../domain/decisions.md#x13)). |
| **A web page, nothing stored** | Used on a phone with no app and nothing kept on the device ([X10](../domain/decisions.md#x10), [ADR 0016](0016-progress-is-reported-not-tracked.md)). |

Who is a Convoy Leader, and how a driver holds a permission on one convoy only, is
[ADR 0010](0010-resource-scoped-permissions.md).

## Alternatives considered

**Keep addresses Ground Officer only, and have the leader told by radio.** The status quo, and the safest in
Freedom. It moves the problem rather than solving it: a Ground Officer in Ukraine must be reachable for every
route decision, the radio is itself an unprotected channel, and the leader has no written route.

**Release only at departure.** Narrower in time. Declined: the leader needs to understand the route while the
convoy is being planned and its stops are being booked.

**Release to every driver.** Declined outright ([decision O1](../domain/decisions.md#o1)). Each additional holder
multiplies the places an address can be lost, and the radio from the leader already gives every driver what they
need.

**Release on nomination.** Chosen first, then replaced by the 14-day window ([X8, X11](../domain/decisions.md#x8)):
a leader nominated months ahead need not hold an address for months.

## Consequences

**The threat surface grows by a person and a device.** The reader is a volunteer driver, on a phone, in a vehicle
that crosses borders. The mitigations above address the system. They do not address a lost phone, a shoulder
surfer, or a leader detained at a border while holding an unlocked session. Session lifetime and re-authentication
on the checklist page are worth deciding as part of the review.

**The audit row needs the reader's capacity.** `ReceiverDetailRepository.ResolveAsync` writes an audit row naming
who read what. It must distinguish a Ground Officer's read from a Convoy Leader's, and which convoy it was for.

**`RedactingActivityProcessor` and the logging rules must cover the new path.** Tests should assert, as the label
tests do, that no address reaches telemetry, logs, a queue message or a document on this path.

**Browser storage is only partly enforceable.** No-store headers and the absence of storage and service-worker
code can be tested. Screenshots and the browser's own history cannot be prevented, and this should be accepted as
a limit, not claimed as a guarantee.

**"Final destination" is not yet well defined.** [P5](../domain/decisions.md#p5) gives each vehicle its own
handover Receiver, which may differ from its boxes' Receivers, so a convoy may have several destinations
([Q-final-destination](../domain/decisions.md#q-final-destination)). Until that is answered, the safe reading is
the **convoy's route and the destinations it genuinely needs to reach**, and not every Receiver's address.

**An address also has to be *entered* before it can be read.** Route stops are addresses on the convoy's route, and
a Ukrainian final destination is a Receiver's detail held in the `sensitive` schema. Which reads come from which
store is an implementation question the review should settle.

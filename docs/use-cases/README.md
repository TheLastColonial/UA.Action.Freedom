# Use case diagrams

PlantUML use case diagrams of **who can do what** in the target design, including the scoped roles: the Convoy
Leader acts on their own convoy only, and a Loader sees only the locations they manage. They complement the policy
matrix in [local authentication](../local-authentication.md), which is what the code enforces today.

| Diagram | Covers | Rules | Plans |
|---|---|---|---|
| [Boxes and donations](boxes-and-donations.puml) | Donations, box intake, attestation, labels, replacement, stock, categories, donor erasure | [Boxes and donations](../domain/boxes-and-donations.md) | [05](../plans/05-item-classification-value.md), [06](../plans/06-donors-donations.md), [16](../plans/16-box-replacement-label.md), [17](../plans/17-scoped-permissions.md) |
| [Convoy operations](convoy-operations.puml) | Vehicles, planning, crew, leader, bookings, sign-off, departure, the road, closing | [Convoy operations](../domain/convoy-operations.md) | [10](../plans/10-route-points-convoy-leader.md) to [14](../plans/14-outcomes-closing.md), [17](../plans/17-scoped-permissions.md) to [19](../plans/19-leader-address-access.md) |
| [Declarations and Receivers](declarations-and-receivers.puml) | The four declarations, who files where, Receiver registration and detail | [Customs declarations](../domain/customs-declarations.md) | [04](../plans/04-receiver-registration.md), [08](../plans/08-declarations-filing.md), [09](../plans/09-declaration-staleness.md) |
| [People and access](people-and-access.puml) | Sign-in, linking a login, roles, Loader assignment, volunteer erasure | [Decisions: identity and access](../domain/decisions.md#identity-and-access) | [02](../plans/02-login-person-link.md), [03](../plans/03-last-changed-audit.md), [17](../plans/17-scoped-permissions.md) |

When a plan changes a permission, it updates the matching diagram here as well as `web/src/auth/policyMatrix.ts`.

Related: [process diagrams](../process/README.md), [state diagrams](../states/README.md),
[domain model](../model/domain-model.puml).

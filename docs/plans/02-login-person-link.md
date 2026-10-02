# 02. Link a login to a person

| | |
|---|---|
| **Branch** | `feat/login-person-link` |
| **Covers** | [O34](../domain/decisions.md#o34), [O35](../domain/decisions.md#o35); groundwork for [ADR 0017](../adr/0017-every-entity-records-its-last-change.md), [ADR 0010](../adr/0010-resource-scoped-permissions.md) and [ADR 0009](../adr/0009-convoy-leader-reads-destination-addresses.md) |
| **Depends on** | Nothing. May run before or after plan 01. |
| **Gate** | Run the `/security-review` skill before opening the PR |

## Context

**No login is linked to a person.** `dbo.Person` holds only `Id`, `CreatedAt` and `ErasedAt`, and `dbo.PersonDetail`
holds names, date of birth, phone, `IsDriver` and `Committed`. Nothing maps a token's subject to a `PersonId`.

The caller is resolved inline, in two places, with the same expression:
`caller.FindFirstValue(ClaimTypes.NameIdentifier) ?? caller.FindFirstValue("sub") ?? "unknown"`.
- `src/UA.Action.Freedom.Api/Convoys/ConvoyEndpoints.cs:325` stores it as insurance `RecordedBySub`;
- `src/UA.Action.Freedom.Api/Receivers/ReceiverEndpoints.cs:93` stores it as the access log `PrincipalId`.

The `"unknown"` fallback can write a fake identity.

Worse, the two attestations that matter most take the person **from the request body**:
`Box.ValidatedByPersonId` and `BoxBayAssignment.AssignedByPersonId` (`src/UA.Action.Freedom.Api/Boxes/BoxRequests.cs`
l.19 and l.28, checked only with `people.ExistsAsync`). A caller can attest in someone else's name.

This plan links logins to people and makes **every "who" come from the login**. Plans 03, 17 and 19 build on it.

### Decisions this plan relies on

| Decision | Effect here |
|---|---|
| [O34](../domain/decisions.md#o34) | `IdentitySubject` on **`PersonDetail`** (erasable), set by an Administrator. |
| [O35](../domain/decisions.md#o35) | A write that records who did it, from an **unlinked** login, is refused with **403 `login-not-linked`**. Never "unknown". |
| [D15](../domain/decisions.md#d15) / volunteer erasure | Erasure deletes `PersonDetail`, so it deletes the link with the personal data. |

## Increments

### Increment 1: the link column

- **Schema:** `database/UA.Action.Freedom.Database/dbo/Tables/PersonDetail.sql` gains
  `IdentitySubject nvarchar(200) NULL` and a filtered unique index `WHERE IdentitySubject IS NOT NULL`.
- **Data:** `src/UA.Action.Freedom.Data/People/PersonRepository.cs` gains `FindBySubjectAsync(subject)`, which returns
  a `PersonId` or nothing. It reads `PersonDetail` only, so an erased person is never found. It also gains
  `LinkLoginAsync(personId, subject)`, which returns `Linked`, `NotFound` or `SubjectInUse`.
- **Tests (RED first):**
  - Integration: link, then find; a second person with the same subject is refused; erasure deletes the link, so the
    subject is no longer found.
  - Mirror this in the in-memory people fake.

### Increment 2: one place that answers "who is calling"

- **Application:** a new `src/UA.Action.Freedom.Application/Abstractions/ICurrentPerson.cs` returns `Linked(PersonId)`
  or `NotLinked`.
- **Api:** a resolver in `src/UA.Action.Freedom.Api/Configuration/` reads the subject (`NameIdentifier`, then `sub`)
  and looks it up through the people port. It is scoped per request and memoised.
- **Tests:** unit tests for linked, unlinked, erased and a missing subject claim (`NotLinked`, never "unknown").

### Increment 3: Administrator links a login, and `/me`

- **Api:**
  - `PUT /people/{id}/login`, Administrator only, body `{ subject }`: 204, 404, or 409 when the subject is in use.
  - `GET /me`, for any authenticated caller: the caller's subject and roles, plus their person id and display name
    when linked. It never returns other personal data.
- **Bootstrap:** an Administrator may link **their own** login, so the first link can be made.
- **Tests:** component tests for the policy (only Administrator may link) and the `/me` contract.

### Increment 4: every write that records "who" uses the resolver

- Replace both inline expressions:
  - insurance `RecordedBy` becomes the linked `PersonId`;
  - the receiver-detail access log records the caller's `PersonId`.
- Decide in the PR whether to keep the subject alongside. The access log is in the `sensitive` schema, so check the
  types and the grants in `database/.../Security/Permissions.sql`.
- An unlinked caller gets **403** with `ProblemDetails` type `login-not-linked`.
- **Tests:** component tests that an unlinked caller is refused on each of these routes, and nothing is written.

### Increment 5: attestations come from the login

- `POST /boxes/{id}/validate` and `PUT /boxes/{id}/bay` **no longer accept** `ValidatedByPersonId` or
  `AssignedByPersonId`. The handler takes the person from `ICurrentPerson`.
- Change `BoxRequests.cs`, its validators, `BoxUseCases.ValidateBoxHandler` and `BoxBayUseCases`.
- The outcome `NoSuchValidator` becomes the 403 path.
- **Tests:**
  - Component: a body field is rejected or ignored (pick one and pin it), and the stored validator is the caller.
  - Integration is unchanged.
  - BDD: `Boxes.feature` and `BoxQrCodes.feature` scenarios that validate or shelve.

### Increment 6: web

- `web/src/api/`: a `me` client.
- `web/src/pages/people/PersonDetailPage.tsx`: "Link login" for an Administrator.
- Remove the validator and assigner pickers from `BoxValidatePanel.tsx` and `BoxBayPanel.tsx`, and show "You will sign
  as …".
- Tests: Vitest with MSW for each.

### Increment 7: local identities and BDD

- In `iac/tofu/keycloak.tf`, the seed users `admin`, `operator` and `groundofficer` have generated subjects.
- **BDD:** a hook reads `GET /me` for each seed login and links it with the Administrator token, creating a person per
  login if needed.
- **Seed:** `database/seed/dev-seed.sql` gains people matching the seed logins, without subjects. The BDD hook or a
  documented one-off step links them.
- Document this in `docs/local-authentication.md`.

## Retires and transitional

- **Retires:**
  - both inline caller expressions and the `"unknown"` fallback;
  - person ids in the validate and bay request bodies.
- **Transitional:** existing insurance rows hold a subject string. The local schema is rebuilt from scratch, so this
  only matters for the type change.

## Docs to update

- `CLAUDE.md`: Auth section (`/me`, the link, 403 `login-not-linked`) and the API list.
- `README.md`: endpoints and the policy matrix.
- `docs/local-authentication.md`: how seed logins are linked.
- `docs/domain/key-concepts.md` § Volunteer erasure: erasure removes the login link.
- `web/src/auth/policyMatrix.ts`, if a new policy is added for linking.
- Gotchas: Keycloak subjects are generated per realm import.

## Risks

- **Security-sensitive.** This changes how every attestation is attributed. Run `/security-review` and record the
  result in the PR.
- **Keycloak subject stability.** Subjects change when the realm is recreated. The BDD hook must link on every run, not
  assume ids.
- **Erasure ordering.** `PersonRepository.DeleteAsync` holds an `UPDLOCK` and falls back to stamping `ErasedAt` on a
  foreign-key violation. Deleting the detail row must still remove the link. Test it.

## Testing

Unit (resolver), Component (policies and 403s), Integration (link and erasure), BDD (validate and shelve with a linked
login), web (link UI, signing as).

## Verification

The standard gates, plus on the local stack:
1. Log in as `operator`. `GET /me` shows it is not linked.
2. As `admin`, link it.
3. Validate a box. The validator is `operator`'s person.
4. Erase that person. `GET /me` for `operator` shows it is not linked again.

## Sequencing

Before [plan 03](03-last-changed-audit.md), which needs `ICurrentPerson`. [Plans 16](16-box-replacement-label.md) and
[17](17-scoped-permissions.md) also depend on it.

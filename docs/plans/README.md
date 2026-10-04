# Implementation plans

A series of plans that implement [ADRs 0004 to 0017](../adr/README.md) and the feature decisions in
[Decisions](../domain/decisions.md) that have no ADR of their own. Each plan is one **branch** and one **PR**. An agent
executes a plan, opens the PR and **stops**. The developer reviews and merges, and only then does the next plan start.

The business rules each plan implements are in [`docs/domain/`](../domain/README.md). The flows are drawn twice:
as calls between people and systems in [`docs/sequences/`](../sequences/README.md), and as role-by-role processes in
[`docs/process/`](../process/README.md), starting with the
[end-to-end overview](../process/00-end-to-end-overview.puml). Lifecycles are in
[`docs/states/`](../states/README.md), permissions in [`docs/use-cases/`](../use-cases/README.md), the target
[domain model](../model/domain-model.puml) is one class diagram, and the time rules are on a worked
[convoy timeline](../timeline/convoy-timeline.puml). When a plan changes a drawn flow, it updates both
diagrams. When a plan and a domain document
disagree, the domain document wins: raise it in the PR rather than building around it.

## Index

| # | Plan | Branch | Covers | Depends on | Gate | Status |
|---|---|---|---|---|---|---|
| 01 | [Crew without legs](01-crew-without-legs.md) | `feat/crew-without-legs` | [ADR 0007](../adr/0007-journey-legs-are-removed-from-the-crew-model.md), [O7](../domain/decisions.md#o7) | – | – | In review |
| 02 | [Link a login to a person](02-login-person-link.md) | `feat/login-person-link` | [O34](../domain/decisions.md#o34), [O35](../domain/decisions.md#o35) | – | `/security-review` before PR | In review |
| 03 | [Who last changed it](03-last-changed-audit.md) | `feat/last-changed-audit` | [ADR 0017](../adr/0017-every-entity-records-its-last-change.md) | 02 | – | In review |
| 04 | [Receiver registration](04-receiver-registration.md) | `feat/receiver-registration` | [ADR 0012](../adr/0012-receiver-registration-gates-convoys-and-boxes.md), [P11](../domain/decisions.md#p11) | 03 | – | In review |
| 05 | [Item classification and value](05-item-classification-value.md) | `feat/item-classification-value` | [ADR 0014](../adr/0014-items-are-classified-by-category-and-valued-in-gbp.md) | 03 | – | In review |
| 06 | [Donors and donations](06-donors-donations.md) | `feat/donors-donations` | [ADR 0013](../adr/0013-donors-are-a-split-identity.md) | 05 | – | In review |
| 07 | [Box allocation and ferry booking](07-box-allocation-ferry.md) | `feat/box-allocation-ferry` | [ADR 0004](../adr/0004-the-manifest-is-the-load-sign-off.md) (cargo), [P1](../domain/decisions.md#p1) | 03 | – | In review |
| 08 | [Declarations and filing mode](08-declarations-filing.md) | `feat/declarations-filing` | [ADR 0005](../adr/0005-declarations-are-per-vehicle-with-derived-staleness.md) (entity), [ADR 0006](../adr/0006-filing-is-manual-by-default.md) | 04, 05, 07 | – | In review |
| 09 | [Declaration staleness](09-declaration-staleness.md) | `feat/declaration-staleness` | [ADR 0005](../adr/0005-declarations-are-per-vehicle-with-derived-staleness.md) (snapshot) | 08 | – | Not started |
| 10 | [Route points and the Convoy Leader](10-route-points-convoy-leader.md) | `feat/route-points-convoy-leader` | [P15](../domain/decisions.md#p15), [D17](../domain/decisions.md#d17), [P14](../domain/decisions.md#p14) | 01, 03 | – | Not started |
| 11 | [Accommodation](11-accommodation.md) | `feat/accommodation` | [P2](../domain/decisions.md#p2), [P8](../domain/decisions.md#p8), [P13](../domain/decisions.md#p13), [P16](../domain/decisions.md#p16), [O4](../domain/decisions.md#o4), [O30](../domain/decisions.md#o30) | 10 | – | Not started |
| 12 | [Budget, costs and equipment](12-budget-equipment.md) | `feat/budget-equipment` | [O12](../domain/decisions.md#o12), [O13](../domain/decisions.md#o13), [O37](../domain/decisions.md#o37) | 03, 05 | – | Not started |
| 13 | [Readiness and departure](13-readiness-departure.md) | `feat/readiness-departure` | [ADR 0008](../adr/0008-readiness-is-computed-and-blocking-rules-are-not-overridden.md), [O36](../domain/decisions.md#o36) | 01, 04, 07, 09, 11, 12 | – | Not started |
| 14 | [Outcomes and closing](14-outcomes-closing.md) | `feat/outcomes-closing` | [ADR 0015](../adr/0015-box-and-vehicle-outcomes-and-convoy-closing.md) | 08, 12, 13 | – | Not started |
| 15 | [Manifest sign-off lifecycle](15-manifest-signoff-lifecycle.md) | `feat/manifest-signoff-lifecycle` | [ADR 0004](../adr/0004-the-manifest-is-the-load-sign-off.md) (lifecycle), [X3](../domain/decisions.md#x3) | 13, 14 | – | Not started |
| 16 | [Box replacement and bilingual label](16-box-replacement-label.md) | `feat/box-replacement-label` | [ADR 0011](../adr/0011-attested-boxes-are-replaced-not-edited.md) | 02, 05, 09 | **Spike + label review** | Not started |
| 17 | [Scoped permissions](17-scoped-permissions.md) | `feat/scoped-permissions` | [ADR 0010](../adr/0010-resource-scoped-permissions.md) | 02, 10 | **Mechanism sign-off** | Not started |
| 18 | [Convoy Leader checklist and progress](18-leader-checklist-progress.md) | `feat/leader-checklist-progress` | [ADR 0016](../adr/0016-progress-is-reported-not-tracked.md), [X2](../domain/decisions.md#x2) | 09, 11, 12, 14, 17 | – | Not started |
| 19 | [Convoy Leader address access](19-leader-address-access.md) | `feat/leader-address-access` | [ADR 0009](../adr/0009-convoy-leader-reads-destination-addresses.md) | 04, 18 | **Security review** | Not started |

Update the **Status** column in the same PR as the plan: *In progress* when the branch is opened, *In review* when the
PR is opened, *Done* is set by the developer on merge.

## Dependencies

```mermaid
flowchart LR
    P01[01 Crew] --> P10[10 Route points + leader]
    P01 --> P13
    P02[02 Login link] --> P03[03 Last changed]
    P02 --> P16
    P02 --> P17
    P03 --> P04[04 Receivers]
    P03 --> P05[05 Items]
    P03 --> P07[07 Allocation + ferry]
    P03 --> P10
    P03 --> P12
    P05 --> P06[06 Donors]
    P05 --> P12[12 Budget + equipment]
    P05 --> P16[16 Box replacement + label]
    P04 --> P08[08 Declarations]
    P05 --> P08
    P07 --> P08
    P08 --> P09[09 Staleness]
    P09 --> P13[13 Readiness + depart]
    P09 --> P16
    P10 --> P11[11 Accommodation]
    P10 --> P17[17 Scoped permissions]
    P04 --> P13
    P07 --> P13
    P11 --> P13
    P12 --> P13
    P13 --> P14[14 Outcomes + closing]
    P08 --> P14
    P12 --> P14
    P14 --> P15[15 Manifest sign-off]
    P13 --> P15
    P14 --> P18[18 Leader checklist]
    P17 --> P18
    P09 --> P18
    P11 --> P18
    P12 --> P18
    P18 --> P19[19 Address access]
    P04 --> P19
```

**Critical path:** 02 → 03 → 07 → 08 → 09 → 13 → 14 → 15. Plans 01 and 02 may be done in either order, and so may 04,
05 and 07. Plans 06, 12 and 16 have slack. The security review that opens plan 19 may be started early, in parallel.

**Every merged plan leaves the system working.** While plans 08 to 14 are in flight, some legacy manifest behaviour
sits alongside the new facts. Each plan has a **Retires and transitional** section that says what it removes and
what it deliberately leaves for a later plan.

## Owner gates

Four plans stop for the owner before writing production code. The gate is the plan's **Increment 0**.

| Plan | Gate | What the agent produces, then stops |
|---|---|---|
| 16 | Translation spike **and** label data-sensitivity review | A spike report and a review document, in a **draft** PR |
| 17 | ADR 0010 mechanism sign-off | The concrete mechanism written into ADR 0010, in a **draft** PR |
| 19 | Security review of ADR 0009 | `docs/security/0009-review.md`, in a **draft** PR |

Plans 02, 17 and 19 also run the `/security-review` skill before the PR is opened. That is a check, not an owner gate.

When an owner signs off a gate, the agent continues on the **same branch**, and the draft PR becomes the plan's PR.

## Coverage

| ADR or decision | Plan |
|---|---|
| [0004](../adr/0004-the-manifest-is-the-load-sign-off.md) | 07 (cargo, ferry), 08 (approval only signs off), 15 (lifecycle) |
| [0005](../adr/0005-declarations-are-per-vehicle-with-derived-staleness.md) | 08 (entity, lifecycle), 09 (staleness), 18 (closing at a crossing) |
| [0006](../adr/0006-filing-is-manual-by-default.md) | 08 |
| [0007](../adr/0007-journey-legs-are-removed-from-the-crew-model.md) | 01 |
| [0008](../adr/0008-readiness-is-computed-and-blocking-rules-are-not-overridden.md) | 01 (insurance), 13 |
| [0009](../adr/0009-convoy-leader-reads-destination-addresses.md) | 19 |
| [0010](../adr/0010-resource-scoped-permissions.md) | 02 (login link), 17 |
| [0011](../adr/0011-attested-boxes-are-replaced-not-edited.md) | 16 |
| [0012](../adr/0012-receiver-registration-gates-convoys-and-boxes.md) | 04 |
| [0013](../adr/0013-donors-are-a-split-identity.md) | 06 |
| [0014](../adr/0014-items-are-classified-by-category-and-valued-in-gbp.md) | 05 |
| [0015](../adr/0015-box-and-vehicle-outcomes-and-convoy-closing.md) | 14, 18 (leader's own actions) |
| [0016](../adr/0016-progress-is-reported-not-tracked.md) | 18 |
| [0017](../adr/0017-every-entity-records-its-last-change.md) | 02, 03 |
| Ferry per vehicle ([P1](../domain/decisions.md#p1)) | 07 |
| Accommodation ([P2](../domain/decisions.md#p2), [P8](../domain/decisions.md#p8), [P13](../domain/decisions.md#p13), [P16](../domain/decisions.md#p16), [O4](../domain/decisions.md#o4), [O30](../domain/decisions.md#o30)) | 11 |
| Route point kinds ([P15](../domain/decisions.md#p15)), leader nomination ([D17](../domain/decisions.md#d17), [P14](../domain/decisions.md#p14)) | 10 |
| Budget, costs, equipment ([O12](../domain/decisions.md#o12), [O13](../domain/decisions.md#o13), [O37](../domain/decisions.md#o37), [P3](../domain/decisions.md#p3)) | 12 |
| Expiry and sensitive goods ([D1](../domain/decisions.md#d1), [D7](../domain/decisions.md#d7), [D16](../domain/decisions.md#d16), [D21](../domain/decisions.md#d21), [D25](../domain/decisions.md#d25)) | 05 (data and warnings), 13 (readiness warnings) |
| Capacity ([D10](../domain/decisions.md#d10), [D18](../domain/decisions.md#d18)) | 07 (warning moves with allocation), 13 |
| Value report ([D29](../domain/decisions.md#d29)) | 05 (values), 14 (closing report), 06 (donor report) |
| Notifications on screen ([O21](../domain/decisions.md#o21)) | 09 (re-declare tasks), 11 (booking tasks) |

**Not planned yet:** [Q-retention](../domain/decisions.md#q-retention) and
[Q-vehicle-equipment](../domain/decisions.md#q-vehicle-equipment) are open questions. Expected boxes
([O22](../domain/decisions.md#o22)) need only the existing box creation and are not a plan of their own.

## Agent execution protocol

Follow this for every plan. It is deliberately the same each time.

### 1. Start from an up-to-date main

```bash
git checkout main && git pull --ff-only
git log --oneline -20          # confirm every plan in "Depends on" has merged
git checkout -b <branch from the plan>
```

If a dependency has not merged, **stop** and say so. Do not stack branches.

### 2. Read before writing

- `CLAUDE.md` at the repository root, and the user's global rules.
- The plan, its ADRs, and the sections of `docs/domain/` it links to.
- [`docs/gotchas-and-open-questions.md`](../gotchas-and-open-questions.md), before any long debugging session.
- Load the **`tdd`** skill. Load **`react-testing`** and **`front-end-testing`** when the plan changes `web/`.

Paths in the plans were correct on 2026-10-02. Check them before editing, because earlier plans move code.

### 3. Gate increment, if the plan has one

Produce what Increment 0 asks for, commit it, push, open a **draft** PR labelled `awaiting-owner`, and **stop**.
Resume only when the owner has signed off in the PR.

### 4. Increments: test-driven, one commit each

- **RED → GREEN → REFACTOR** for every behaviour. No production code without a failing test.
- **One commit per green increment**, on the plan's branch. The PR review is the approval for these commits. Never
  commit to `main`.
- Test order within an increment: **Unit → Component → Integration → BDD → web (Vitest + MSW) → Playwright**, as far
  as the increment reaches.
- **A fake must enforce every rule its SQL does.** Change `tests/UA.Action.Freedom.Tests.Component/InMemory*Repository`
  in the same commit as the SQL it mirrors.
- **Edit `.feature` files, never the generated `.feature.cs`.**
- **Schema:** end-state `CREATE`s only, no `ALTER`, no data moves. Write CHECKs in normalised form (`>= 0 AND <= 3`).
  Key columns that are `varchar(32)` are passed with `SqlKey.Of(...)`.
- Zero build warnings, always.

### 5. Documents change with the code

In the same branch: `README.md` (structure, endpoints, policy matrix), `CLAUDE.md`, `docs/domain/key-concepts.md`,
`docs/gotchas-and-open-questions.md`, the ADR's "Not yet implemented" note, the **Status** in this index, and
`web/src/auth/policyMatrix.ts` whenever a policy changes. The rows of
[Consequences and amendments due](../domain/decisions.md#consequences-and-amendments-due) a plan satisfies are
removed from that table.

### 6. Gates before the PR

```bash
dotnet build UA.Action.Freedom.slnx                    # zero warnings
dotnet test --solution UA.Action.Freedom.slnx

cd web
npx prettier --write <changed files>                   # npm run format:write fails in Git Bash on Windows
npm run verify                                         # typecheck, lint, format check, test, build
cd ..

cd iac/local && docker compose down -v && docker compose up -d --wait
cd ../tofu && tofu apply -auto-approve
cd ../local && docker compose build app manifest-worker customs-worker db-deploy \
  && docker compose up -d --wait && docker compose up db-seed
cd ../..
FREEDOM_REQUIRE_INTEGRATION=true dotnet test --project tests/UA.Action.Freedom.Tests.Integration/UA.Action.Freedom.Tests.Integration.csproj
FREEDOM_REQUIRE_INTEGRATION=true dotnet test --project tests/UA.Action.Freedom.Tests.BDD/UA.Action.Freedom.Tests.BDD.csproj
cd web && npm run e2e
```

A second publish of the database must be a no-op, as the `Database` workflow requires. When a schema change will not
apply in place, rebuild the local database from scratch.

### 7. Open the PR

```bash
git push -u origin <branch>
gh pr create --base main --title "<plan number>: <plan title>" --body-file <file>
```

The PR body has these sections:
- **Covers**: the ADRs and decisions.
- **Increments**: one line each.
- **Retired** and **Still transitional**.
- **Tests**: the layers touched and the gate output.
- **Gate evidence**, if any.
- **Risks**.

It ends with:

```
🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

Each commit message ends with the `Co-Authored-By` line the session's attribution instructions give.

### 8. Stop

Do not start the next plan. The developer merges, and the next plan begins again at step 1.

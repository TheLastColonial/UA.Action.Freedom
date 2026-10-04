---
name: agent-worktree
description: Create, run and clean up an isolated git worktree plus its own local docker stack (own ports, containers, database, Keycloak) so several agents can change this repo at the same time without clashing. Use when starting work in parallel with other agents, when asked for a "new worktree", "isolated stack", "agent slot", or when ports/containers collide.
---

# Parallel agents: one worktree + one stack each

Full guide: `docs/parallel-agents.md`. This is the short, repeatable procedure.

## Rules

1. **Never work in the main checkout** (slot 0, ports 8080/8081/1433...) if another agent might be. Work only inside your own worktree.
2. **Tear down with `agent.ps1 down [-Volumes]`, never raw `docker compose down -v`** (it clears the stale tofu state too).
3. **Never run bare `docker compose`, `tofu apply` or `dotnet test` against ports you did not claim.** Use `scripts/agent/agent.ps1` (or `agent.sh`) for the stack; load `.agent/env.ps1` before tests.
4. One agent = one name = one branch (`agent/<name>`) = one slot (1-9) = one PR.
5. Do not hand-edit `iac/local/.env`, `iac/tofu/terraform.tfvars` or `.agent/*` in a worktree: they are generated (re-run `new` on a fresh name instead).

## Create

```
pwsh scripts/agent/agent.ps1 new <name> [-Base main] [-Slot n] [-Up]      # PowerShell
scripts/agent/agent.sh new <name> [--base main] [--slot n] [--up]         # bash (wraps the same script)
```

`<name>`: lowercase letters/digits/dashes, starts with a letter, max 20 (e.g. `plan-09`). Creates `../UA.Action.Freedom.worktrees/<name>`, claims the lowest free slot (ports `20000 + slot*100 + offset`), writes `.env`, `terraform.tfvars`, `.agent/env.*`. Then `cd` into the worktree and continue there.

## Run

| Need | Command (inside the worktree, or add `<name>`) |
| --- | --- |
| Start stack with hot reload + provision | `agent.ps1 up` (first start compiles for a few minutes) |
| Baked images like CI | `agent.ps1 up -NoHotReload` (then `docker compose build` yourself) |
| Test environment | `. ./.agent/env.ps1` (bash: `. ./.agent/env.sh`), then `dotnet test --solution UA.Action.Freedom.slnx` |
| Database schema changed | `agent.ps1 db` (re-publishes the dacpac) |
| Where is everything | `agent.ps1 list`, `agent.ps1 ports`, `agent.ps1 status` |
| Regenerate generated files (after the scripts change) | `agent.ps1 refresh` |
| Stop / wipe data | `agent.ps1 down` / `agent.ps1 down -Volumes` (also resets tofu state) |

Code edits to `.cs` files in `src/` reload by themselves (`dotnet watch`); web edits hot-reload at the Vite URL `http://localhost:<VITE_PORT>/app/`. Changes to `Program.cs`/DI usually trigger an automatic restart; if the watcher wedges, `docker compose restart app` from `iac/local`.

## Finish

After your PR is merged: `pwsh scripts/agent/agent.ps1 remove <name>` from the **main** checkout. It refuses if the worktree has uncommitted work (use `-Purge` only to discard deliberately), stops the stack, wipes its volumes and images, removes the worktree, deletes the branch if merged and frees the slot.

## Troubleshooting

- Services restart in a loop / `Cannot allocate memory`: the Docker VM is out of RAM (about 3.5 GB per agent stack). `docker run --rm alpine free -m`, then `agent.ps1 down` an idle stack or raise Docker's memory limit.
- "Slot N has ports in use": something else on the machine owns a port in that block; pass `-Slot` for another.
- API never gets healthy in hot-reload mode: `docker logs freedom-<name>-app` (restore/compile errors show there).
- Keycloak login redirects to the wrong port: the SPA's authority is set from `KEYCLOAK_PORT` (Vite env / `VITE_OIDC_AUTHORITY` build arg); re-run `up` after changing ports.

# Working in parallel: one worktree and one stack per agent

Several agents (or people) can change this repository at once without touching each other's
git history, containers, ports, databases or build output. Each one gets:

| Isolated thing | How |
| --- | --- |
| Git history and files | A **git worktree** at `../UA.Action.Freedom.worktrees/<name>` on branch `agent/<name>` |
| Containers, volumes, network | Its own compose project `freedom-<name>` and container prefix `freedom-<name>-` |
| Images | Tagged `ua-action-freedom/<svc>:<name>` so one agent's build never replaces another's |
| Host ports | A **slot** (1-9): every published port is `20000 + slot*100 + offset` |
| Database, realm, queues, blobs | Their own SQL Server, Keycloak, Azurite (a full stack per agent) |
| Tofu state | Per worktree already (`iac/tofu/terraform.tfstate` is untracked) |
| Build output | Per worktree `bin/` `obj/`; in the containers, `ArtifactsPath` on named volumes |

The main checkout is **slot 0** and keeps today's defaults (8080, 8081, 1433, ...), so CI, the
README and every existing habit are unchanged.

## Everything goes through one script

`scripts/agent/agent.ps1` (PowerShell 7) and `scripts/agent/agent.sh` (a wrapper that translates
`--kebab-flags` and calls the same script, so the two cannot drift). The `agent-worktree` Claude
skill (`.claude/skills/agent-worktree/SKILL.md`) is the short checklist agents follow.

```powershell
pwsh scripts/agent/agent.ps1 new alpha -Up          # worktree + slot + config + stack (hot reload)
cd ../UA.Action.Freedom.worktrees/alpha
. ./.agent/env.ps1                                  # point tests and Playwright at *this* stack
dotnet test --solution UA.Action.Freedom.slnx
pwsh scripts/agent/agent.ps1 list
pwsh scripts/agent/agent.ps1 down                   # stop (keeps data); -Volumes wipes data + tofu state
pwsh scripts/agent/agent.ps1 remove alpha           # from the main checkout, after the PR is merged
```

| Command | Does |
| --- | --- |
| `new <name> [-Base main] [-Slot n] [-Up] [-NoHotReload]` | `git worktree add -b agent/<name>`, claims the lowest free slot (checks its ports are free), writes the generated files below. `-Up` also starts and provisions the stack |
| `up [<name>] [-NoHotReload]` | `docker compose up -d --wait` (with `docker-compose.dev.yml` unless `-NoHotReload`), then `tofu init && tofu apply` |
| `down [<name>] [-Volumes]` | `docker compose down`; `-Volumes` also drops volumes and deletes `terraform.tfstate*` (a stale state after a volume wipe silently skips provisioning) |
| `db [<name>]` | Re-publishes the dacpac into the running stack (the schema is not hot-reloadable) |
| `list` / `status` / `ports` / `env` | Show agents and slots / compose services / the port block / how to load the test env |
| `refresh [<name>]` | Regenerate `.env`, tfvars, `.agent/` and the dev container config for an existing agent (after the scripts or the main `.env` change) |
| `remove <name> [-Purge]` | Refuses on uncommitted work; stops the stack, wipes volumes and images, removes the worktree, deletes the branch if merged, frees the slot. `-Purge` discards work and force-deletes the branch |

`<name>` may be omitted when you are inside a worktree. Names: lowercase letters, digits, dashes,
start with a letter, at most 20 characters.

### What `new` generates (all untracked)

- `iac/local/.env`: the secrets copied from the main checkout's `.env` (or `.env.example`), plus
  `COMPOSE_PROJECT_NAME`, `FREEDOM_PREFIX`, `FREEDOM_IMAGE_TAG` and every `*_PORT`.
- `iac/tofu/terraform.tfvars`: `keycloak_url`, `edge_url`, `vite_dev_url`, Azurite endpoints,
  `mssql_container` and the stub ports, matching the slot.
- `.agent/env.ps1`, `.agent/env.sh`: `FREEDOM_BASE_URL`, `FREEDOM_OIDC_URL`,
  `PLAYWRIGHT_BASE_URL`, `ConnectionStrings__Freedom`, `ConnectionStrings__FreedomSensitive`,
  `Storage__ConnectionString`, `FREEDOM_REQUIRE_INTEGRATION=true`. **Source one before running
  Integration, BDD or Playwright tests**; without it they fall back to the main stack's ports.
- `.agent/env.container.sh`: the same with `host.docker.internal` for use inside the dev container.
- `.devcontainer/agent/devcontainer.json`: a per-worktree dev container config (below).
- The slot registry is `<main .git>/agent-slots.json`, shared by every worktree and guarded by a lock.

## Ports

Slot 0 is the existing defaults. Slot N (1-9) uses `20000 + N*100 + offset`:

| Offset | Variable | Slot 0 | Slot 1 | Slot 2 |
| --- | --- | --- | --- | --- |
| 0 | `EDGE_HTTP_PORT` (app, behind Traefik) | 8080 | 20100 | 20200 |
| 1 | `EDGE_HTTPS_PORT` | 8443 | 20101 | 20201 |
| 2 | `EDGE_DASHBOARD_PORT` | 8090 | 20102 | 20202 |
| 3 | `KEYCLOAK_PORT` | 8081 | 20103 | 20203 |
| 4 | `WIREMOCK_PORT` | 8082 | 20104 | 20204 |
| 5 | `GRAFANA_PORT` | 3000 | 20105 | 20205 |
| 6 / 7 | `OTLP_GRPC_PORT` / `OTLP_HTTP_PORT` | 4317 / 4318 | 20106 / 20107 | 20206 / 20207 |
| 8 | `MAILPIT_UI_PORT` | 8025 | 20108 | 20208 |
| 9 | `MSSQL_PORT` | 1433 | 20109 | 20209 |
| 10 / 11 / 12 | `AZURITE_BLOB/QUEUE/TABLE_PORT` | 10000-10002 | 20110-20112 | 20210-20212 |
| 13 | `VITE_PORT` (hot-reload web UI) | 5173 | 20113 | 20213 |

`agent.ps1 ports [<name>]` prints your block. The formula is pinned by
`scripts/agent/tests/Agent.Lib.Tests.ps1` (Pester 5: `Invoke-Pester scripts/agent/tests`).

## Hot reload

`iac/local/docker-compose.dev.yml` layers over the substrate and `agent.ps1 up` uses it by
default:

- `app`, `customs-worker`, `manifest-worker` run `dotnet watch run` in the SDK image against the
  bind-mounted worktree. Saving a `.cs` file rebuilds and restarts only that process.
  `DOTNET_USE_POLLING_FILE_WATCHER=1` is required because Windows/macOS bind mounts deliver no
  inotify events. `bin/obj` are redirected with `ArtifactsPath` to named volumes so Linux build
  output never lands in your checkout.
- `web` runs the Vite dev server (`http://localhost:<VITE_PORT>/app/`, HMR) proxying API calls to
  `app`. The API container in this mode has no built SPA, so use the Vite URL, not `/app` on the edge.
- The first start restores and compiles the whole graph (several minutes; `start_period: 240s`).
  Later restarts take seconds.
- Not hot-reloadable: the schema (`agent.ps1 db`), the Keycloak realm/containers/queues
  (`tofu apply`, re-run by `up`), anything under `iac/`.
- `-NoHotReload` runs the baked images exactly as CI does (build them first:
  `cd iac/local && docker compose build app customs-worker manifest-worker`). Use it before a PR
  if you changed a Dockerfile, project references or anything the watcher might hide.

## Dev container (experimental)

`.devcontainer/` is a portable toolchain: .NET 10 SDK, Node 24, PowerShell, git and the docker
CLI (docker-outside-of-docker). `new` generates `.devcontainer/agent/devcontainer.json` for the
worktree, which mounts the worktree and the main repo's `.git` (a worktree's `.git` file points at
an absolute host path that does not exist in the container, so `GIT_DIR`/`GIT_COMMON_DIR` are set
instead) and sets `FREEDOM_AGENT`/`FREEDOM_SLOT`. Open the worktree in VS Code and choose
"Dev Containers: Reopen in Container", picking the `agent` configuration.

Scope on purpose: the container is for **editing, building and testing**. Start and stop the stack
on the host with `agent.ps1`, because the compose files bind-mount host paths that a container
would resolve wrongly. From inside, `. .agent/env.container.sh` points the test projects at the
host's published ports through `host.docker.internal`.

## Rules for agents

1. Work only in your worktree; never in the main checkout, never in another agent's worktree.
2. Never run bare `docker compose`, `tofu apply` or tests that need infrastructure against ports
   you do not own. Use `agent.ps1`; source `.agent/env.*` first.
3. One agent, one name, one branch, one PR. The branch is `agent/<name>`; rename it before opening
   the PR if the plan wants another name (`git branch -m`).
4. Do not edit generated files (`iac/local/.env`, `terraform.tfvars`, `.agent/`). To change a
   secret, edit the main checkout's `.env` and create a new agent.
5. When the PR merges, `agent.ps1 remove <name>` from the main checkout.
6. Merge conflicts in shared files (`CLAUDE.md`, `README.md`, `docs/`) are expected when two
   agents add to the same section: rebase on `main` before opening the PR.

## Troubleshooting

- **`Slot N has ports in use`**: another process owns a port in that block. Pick `-Slot`.
- **Memory (the one that bites)**: measured on a 16 GB Docker VM, an *uncapped* stack is 4-6 GB
  (an idle dev-mode Keycloak alone grew past 2.7 GB, SQL Server takes what it is offered, the LGTM
  telemetry container ~0.9 GB). With main plus two uncapped agent stacks the VM hit 13.4 GB, swap
  filled and everything started failing with `IOException: Cannot allocate memory` (`dotnet watch`'s
  directory poll, then restart loops), which looks like a watcher bug and is not. So agent stacks are
  capped by the generated `.env` (`KEYCLOAK_JAVA_HEAP="-Xms128m -Xmx640m"`, `MSSQL_MEMORY_LIMIT_MB=1024`),
  which brings one to roughly 3.5 GB; `up` warns when the Docker VM cannot fit one more. Budget
  about 3.5 GB per agent plus whatever the main stack uses, raise the Docker Desktop / `.wslconfig`
  memory limit accordingly, and `agent.ps1 down` stacks you are not using.
- **Dev container: `git status` shows every file modified**: the container's git has no
  `core.autocrlf`, unlike Windows. Use the host for git commands, or pass `-c core.autocrlf=true`.
  Do not set it in the container: `GIT_DIR` points at the shared main `.git`, so it would change the host's.
- **Provisioning skipped after a volume wipe**: `down -Volumes` deletes tofu state for that reason;
  if you wiped volumes by hand, `rm iac/tofu/terraform.tfstate*` first.
- **API unhealthy in hot-reload mode**: `docker logs freedom-<name>-app`. A compile error stops
  `dotnet watch` waiting for a fix; saving a corrected file resumes it.
- **Login redirects to the wrong port**: the SPA authority comes from `KEYCLOAK_PORT`
  (Vite env, or the `VITE_OIDC_AUTHORITY` build arg for the baked image); re-run `up`.
- **Tearing down by hand**: prefer `agent.ps1 down` (`-Volumes` also deletes the tofu state, without
  which the next `up` skips provisioning). A bare `docker compose down` inside a worktree does target
  that agent's stack, and also sees the Vite `web` service, because the generated `.env` carries
  `COMPOSE_FILE=docker-compose.yml:docker-compose.dev.yml` (`up -NoHotReload` rewrites it to the base file
  only). It will not delete `terraform.tfstate*`, and run from the wrong directory it acts on that
  directory's stack.
- **`docker compose` seems to hit the wrong stack**: compose picks the project from
  `iac/local/.env` in the current directory; check `COMPOSE_PROJECT_NAME` there and which worktree
  your shell is in.
- **Tofu serialisation**: OpenTofu runs its local-exec steps one at a time because of Docker
  Desktop's loopback proxy (`iac/README.md`). Do not provision two stacks from the same shell in
  parallel; separate shells are fine.

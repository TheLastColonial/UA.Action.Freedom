# UA Action Freedom — Operator UI

React + Vite single-page app for the Freedom API. TypeScript strict, TDD, behaviour-driven
tests. Served **same-origin** by the API host under `/app` (built into `wwwroot/app` at
image-build time); in development it runs on the Vite dev server and proxies API calls to
the edge, so there is no CORS.

## Prerequisites

- **Node 22 LTS** — `nvm use` reads `.nvmrc`.

## Scripts

| Command               | What it does                                                                                                                         |
| --------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| `npm run dev`         | Vite dev server at <http://localhost:5173/app/>. API paths are proxied to `VITE_API_PROXY_TARGET` (default `http://localhost:8080`). |
| `npm run build`       | `tsc -b` then `vite build` → `dist/` (referenced by the API Dockerfile).                                                             |
| `npm run typecheck`   | `tsc -b --noEmit` across all project references.                                                                                     |
| `npm run lint`        | ESLint (flat config, `typescript-eslint` strict + stylistic, a11y, testing-library, tanstack-query).                                 |
| `npm run format`      | Prettier check. `npm run format:write` to fix.                                                                                       |
| `npm run test`        | Vitest **Browser Mode** (real Chromium) + `vitest-browser-react` + Testing Library + MSW.                                            |
| `npm run e2e`         | Playwright smokes against the running docker stack. Self-skips when the stack is down.                                               |
| `npm run e2e:install` | One-time download of the Playwright browser.                                                                                         |
| `npm run verify`      | typecheck → lint → format → test → build. This is what CI's `frontend` job runs.                                                     |

## Auth

Authorization Code + PKCE against the public Keycloak client `freedom-spa` (provisioned by
`iac/tofu/keycloak.tf`). The access token is held in memory and renewed silently; it is sent
as `Authorization: Bearer`. Sign in as one of the seed logins (`admin` / `operator` /
`groundofficer`, password `password`) — see `docs/local-authentication.md`.

## Layout

```
src/
  auth/        oidc config, useAuth, policyMatrix (mirrors the 15-policy matrix; API still enforces)
  api/         apiFetch wrapper + typed verbs, Zod schemas, per-slice query/mutation hooks
  components/  app shell, data table, forms, error/cold-start UX
  pages/       one folder per slice (vehicles, people, convoys, receivers, boxes, manifests)
  test/        renderWithProviders, MSW handlers + factories
e2e/           Playwright smokes + auth setup
```

## Adding a slice

_(Filled in with the Vehicles reference implementation.)_ Each slice adds: Zod schemas in
`src/api/schemas/<slice>.ts`, client + hooks in `src/api/<slice>.ts`, pages under
`src/pages/<slice>/`, MSW handlers in `src/test/msw/handlers/<slice>.ts`, a Vitest Browser
test per page, and one Playwright smoke.

## API response conventions the client encodes

- List endpoints return a bare array; paging is `?page=&pageSize=` (1-based), no total count.
- `POST` create → `201` + `Location` header, **empty body** — the new id is read from `Location`.
- `PUT` / `DELETE` / state transitions → `204`.
- `400` → `application/problem+json` with an `errors` map keyed by PascalCase field.
- `409` / domain-rule failures → `application/problem+json` with a human `detail`.
- Sub-resource collections: `404` (empty body) = parent missing; `200 []` = parent exists, empty.

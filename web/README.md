# UA Action Freedom — Operator UI

React + Vite single-page app for the Freedom API. TypeScript strict, TDD, behaviour-driven
tests. Served **same-origin** by the API host under `/app` (built into `wwwroot/app` at
image-build time); in development it runs on the Vite dev server and proxies API calls to
the edge, so there is no CORS.

## Prerequisites

- **Node 22 LTS** — `nvm use` reads `.nvmrc`.
- **Playwright Chromium** — `npx playwright install chromium` (used by both `npm run test`,
  which runs in a real browser, and `npm run e2e`). `npm run e2e:install` adds OS deps.

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
  components/  app shell, data table, pagination, form/fields, error/cold-start UX
  pages/       one folder per slice (vehicles, people, convoys, receivers, boxes, manifests)
  styles/      tokens.css (light + dark) + global.css
  test/        renderWithProviders, MSW handlers + factories
e2e/           auth.setup.ts (one PKCE login per seed user), per-role + per-slice @smoke specs
```

Styling is **plain scoped CSS** — a `Component.css` imported for its side effect, with
semantic class names (not CSS Modules: `noPropertyAccessFromIndexSignature` fights the
untyped `styles.x` object).

## Adding a slice

**Vehicles (`src/pages/vehicles/`, `src/api/vehicles.ts`) is the reference — copy its shape.**
Each slice adds, in order:

1. **Schemas** — `src/api/schemas/<slice>.ts`: a `z.object` for the read model (optional
   scalars are `.nullable()` — the API sends `null`, not omitted) and a `type` for the
   request DTO (optional fields are `field?: T`, omitted not null). Enums come from
   `src/api/schemas/common.ts` and must match the C# members by name.
2. **Form model** — `src/pages/<slice>/<slice>FormModel.ts` (lowercase, to avoid a
   case-collision with the `.tsx` component): a `FormValues` type of all strings/booleans,
   `empty…()`, `…ToFormValues(readModel)`, a pure `…FormToRequest(values)` mapper (trim,
   omit empty, coerce numbers), and a validation-only Zod schema whose output type equals
   its input type (no transform — keeps react-hook-form happy). All pure → unit-tested.
3. **API module** — `src/api/<slice>.ts`: `fetch…` / `create…` / `update…` / `delete…`
   built on `src/api/http.ts`, then `use…` query/mutation hooks keyed with `qk.<slice>`
   from `src/api/queryKeys.ts`. Mutations invalidate the narrowest safe prefix.
4. **Components** — reuse `DataTable`, `Pagination`, `Gate`, `form/fields`. The shared
   `<SliceForm>` takes `mode`, `initialValues`, `submitting`, `errorMessage` (from a 409
   `ApiDomainProblem`), `fieldErrors` (from a 400 `ApiValidationProblem`, PascalCase keys
   mapped through `problemFieldToFormPath`), and `onSubmit`.
5. **Pages + routes** — `…ListPage` (URL `?page`, "New" behind `Gate`), `…DetailPage`
   (`ApiNotFound` → `<NotFound/>`, Edit/Delete behind `Gate`), `…CreatePage` (on 201,
   navigate to the id from `Location`), `…EditPage`. Export a `RouteObject[]` from
   `src/pages/<slice>/routes.tsx` and mount it in `src/routes.tsx`.
6. **MSW** — `src/test/msw/<slice>.ts` exporting `…Api(seed)` → `{ db, handlers }` matching
   the real routes and status codes; `src/test/factories/<slice>.ts` for fully-defaulted
   read models.
7. **Tests** — one Vitest Browser file per page (`renderWithProviders(null, { routes,
route, roles })`): list renders/empty/error/pagination/role-gated "New"; create shows
   client validation, maps a mocked 400 to a field, follows `Location` on success, shows a
   409 detail verbatim; edit pre-populates and navigates back; detail renders / 404 / hides
   actions for the wrong role.
8. **Smoke** — one `e2e/<slice>.smoke.spec.ts` (`@smoke`), self-skipping via
   `stackIsUp()`, navigating **in-app** (click links, never `page.goto` between SPA pages —
   a full reload drops the in-memory token). A smoke that switches seed users calls
   `signIn` (which clears cookies); one that stays as a single user can `test.use` a
   storage state from `auth.setup.ts`.

### Slice-specific patterns to copy

- **Sub-resource tab pages** (`convoys`, `manifests`) — the detail page keeps the active
  tab in `?tab=` and renders one panel component per tab; each panel owns its own query.
- **A published/validated/frozen aggregate** (`convoys` truck list, `boxes` validate,
  `manifests` GMR) — the freezing `POST` is its own action, and every editing control
  reads the computed `truckListPublished` / `validated` / `frozen` flag to disable itself.
- **A state machine** (`manifests`) — the legal edges live as a pure table in
  `pages/manifests/transitions.ts` mirroring the C# `ManifestTransitions`, unit-tested
  edge-by-edge; the panel renders `availableTransitions(ctx)` as buttons.

- **A non-JSON document + print** (`boxes` QR label) — `getText` in `src/api/http.ts`
  (`expect: 'text'`) fetches the label SVG with the bearer token; `BoxQrCodePanel` renders it
  as an `<img>` data URL and a "Print label" button calls `window.print()`. Printing just the
  label is a `@media print` rule in `BoxQrCodePanel.css` that hides everything and re-reveals
  the `.qr-panel__print` region. A `GET /boxes/{id}/qr-code` that 404s means "no label", not an
  error — `fetchBoxQrCode` maps it to `null`. Issue/revoke are behind `Gate policy="boxes:write"`;
  print is visible to any `boxes:read` role.

## Receiver delivery detail (Ground Officer only)

`src/api/receiverDetail.ts` is deliberately isolated: it is imported only by
`pages/receivers/ReceiverSensitivePanel.tsx`, uses **no React Query** (nothing is cached),
and every `revealReceiverDetail(ref, reason)` call is a fresh, server-audited round trip.
The reason is collected in a modal that states the access is logged; it never enters the
URL or a query key. `src/api/schemas/receivers.ts` has organisation and region only — list
and detail code cannot leak an address because the type has no address field.

## API response conventions the client encodes

- List endpoints return a bare array; paging is `?page=&pageSize=` (1-based), no total count.
- `POST` create → `201` + `Location` header, **empty body** — the new id is read from `Location`.
- `PUT` / `DELETE` / state transitions → `204`.
- `400` → `application/problem+json` with an `errors` map keyed by PascalCase field.
- `409` / domain-rule failures → `application/problem+json` with a human `detail`.
- Sub-resource collections: `404` (empty body) = parent missing; `200 []` = parent exists, empty.
- Document endpoints (the box QR label) return `image/svg+xml`, read as text via `getText`.

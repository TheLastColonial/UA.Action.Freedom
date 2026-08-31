# Authenticating with the API locally

Every route except `/health/live` and `/health/ready` needs a bearer token. Locally those come
from **Keycloak**, which stands in for Microsoft Entra External ID — see
[`../iac/README.md`](../iac/README.md) for what else the simulation covers.

The important thing to know up front: **the token you get is shaped exactly like the one Entra
will issue.** Roles arrive in a flat `roles` claim either way, so the authorization policies you
exercise here are the ones that will run in Azure. Nothing about auth is faked; only the issuer
differs.

---

## Prerequisites

The stack has to be up and provisioned:

```bash
cd iac/local && cp .env.example .env && docker compose up -d --wait
cd ../tofu   && tofu init && tofu apply
```

`tofu apply` is what creates the realm, the client, the roles and the seed logins. Without it
Keycloak is running but empty, and every token request returns `invalid_client`.

Check it worked:

```bash
curl http://localhost:8080/health/ready     # every check Healthy
```

---

## The five things you need

| | Value | Where it comes from |
| --- | --- | --- |
| Token endpoint | `http://localhost:8081/realms/freedom/protocol/openid-connect/token` | `KEYCLOAK_PORT`, `realm_name` |
| Client id | `freedom-app` | `oidc_client_id` in `iac/tofu/variables.tf` |
| Client secret | `local-freedom-client-secret` | `OIDC_CLIENT_SECRET` in `iac/local/.env` |
| Username | `admin`, `operator` or `groundofficer` | seeded by `iac/tofu/keycloak.tf` |
| Password | `password` | `test_user_password` |

The API itself is at **`http://localhost:8080`** (through the Traefik edge). If you are running it
straight from the IDE with `dotnet run` instead, see [Running the API outside
Docker](#running-the-api-outside-docker) below — it needs configuration the container gets from
compose.

> None of these are secrets. They are the local stand-in for what Key Vault holds in the target
> design. Do not reuse them anywhere reachable from outside your machine.

---

## Getting a token

### curl

The bash examples pipe through [`jq`](https://jqlang.github.io/jq/) to pull the token out of the
response. It is not installed by default on Windows — `winget install jqlang.jq`, or use the
PowerShell version below, which needs nothing extra.

```bash
TOKEN=$(curl -s -X POST \
  http://localhost:8081/realms/freedom/protocol/openid-connect/token \
  -d grant_type=password \
  -d client_id=freedom-app \
  -d client_secret=local-freedom-client-secret \
  -d username=operator \
  -d password=password \
  -d scope=openid | jq -r .access_token)

curl -s -H "Authorization: Bearer $TOKEN" 'http://localhost:8080/vehicles?pageSize=5'
```

### PowerShell

```powershell
function Get-FreedomToken($username = 'operator') {
    $body = @{
        grant_type    = 'password'
        client_id     = 'freedom-app'
        client_secret = 'local-freedom-client-secret'
        username      = $username
        password      = 'password'
        scope         = 'openid'
    }
    (Invoke-RestMethod -Method Post `
        -Uri 'http://localhost:8081/realms/freedom/protocol/openid-connect/token' `
        -Body $body -ContentType 'application/x-www-form-urlencoded').access_token
}

$token = Get-FreedomToken 'operator'
Invoke-RestMethod -Uri 'http://localhost:8080/vehicles?pageSize=5' `
    -Headers @{ Authorization = "Bearer $token" }
```

### `.http` file

`src/UA.Action.Freedom.Api/UA.Action.Freedom.Api.http` already does this. It has a named `token`
request and every following call reuses `{{token.response.body.access_token}}`, so in Visual
Studio, Rider or the VS Code REST Client you send the token request once and then click through
the rest.

Tokens last **15 minutes**. When calls start returning 401, fetch another.

### Through the operator UI

The web UI (`web/`) never uses the password grant. It signs in with **Authorization Code +
PKCE** against a separate **public** Keycloak client, `freedom-spa` (no client secret), which
`tofu apply` provisions alongside `freedom-app`. Open <http://localhost:8080/app/> (or
<http://localhost:5173/app/> when running `npm run dev` in `web/`), sign in as one of the
three seed logins, and the browser is redirected back with a token the UI keeps in memory
and renews silently. The token carries the same flat `roles` claim as a password-grant
token, so everything in "Which role can do what" below applies unchanged. The UI's own
`POLICY_MATRIX` mirrors that table — it hides what a role cannot do, and the API still
enforces it.

---

## The three seed logins

They are named for what they can do, not for people — they are fixtures, and seeding realistic
volunteer names would put invented personal data in version control for no benefit.

| Login | Roles | Use it for |
| --- | --- | --- |
| `admin` | `Administrator` | Approving manifests, managing volunteers — anything an Administrator alone may do. |
| `operator` | `Dispatcher`, `Loader`, `Purchaser` | The day-to-day operational path. One login walks the whole convoy workflow. |
| `groundofficer` | `GroundOfficer` | Receivers, and **the only login that can resolve a Ukrainian delivery address.** |

`groundofficer` is deliberately isolated: it holds *no* other role, so it cannot read vehicles,
volunteers, convoys, boxes or manifests. That mirrors the segregation the role carries in
production, and it means "can a Ground Officer see X?" is answerable by just trying it.

Equally, **no other login can reach receiver detail — not even `admin`.** Administering access is
not the same as holding it.

---

## What is in the token

```
iss   : http://localhost:8081/realms/freedom
aud   : account
sub   : 5be71467-97fa-4c13-a208-46f0279d8812
roles : Purchaser, Loader, Dispatcher
```

Three details matter, and each corresponds to a line in
`src/UA.Action.Freedom.Api/Configuration/AuthenticationExtensions.cs`:

- **`roles` is a flat claim, not a scope or a nested object.** `RoleClaimType = "roles"` and
  `MapInboundClaims = false` are both required. Without the second, .NET rewrites `roles` to the
  WS-Federation role URI and *every policy fails* while the token looks perfectly valid.
- **`aud` is `account`**, which is Keycloak's default and not something Freedom issued. Audience
  validation is therefore only switched on when `Oidc:Audience` is explicitly set. In Azure it
  will be set, and this is the line to revisit.
- **`sub` is the principal id** written to the receiver access log. It comes from the token, never
  from a request body — an audit trail a caller could write their own name into would not be one.

To decode a token yourself, paste it into [jwt.io](https://jwt.io), or:

```powershell
$p = $token.Split('.')[1].Replace('-','+').Replace('_','/')
switch ($p.Length % 4) { 2 { $p += '==' } 3 { $p += '=' } }
[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p)) | ConvertFrom-Json
```

---

## Which role can do what

Policies live in `AddFreedomAuthorization()`. A request with no token is **401**; a request with a
token lacking the role is **403**.

| Policy | Administrator | Dispatcher | Loader | Purchaser | GroundOfficer |
| --- | :-: | :-: | :-: | :-: | :-: |
| `vehicles:read` | ✓ | ✓ | ✓ | ✓ | |
| `vehicles:write` | ✓ | | | ✓ | |
| `people:read` | ✓ | ✓ | ✓ | ✓ | |
| `people:write` | ✓ | | | | |
| `convoys:read` | ✓ | ✓ | ✓ | ✓ | |
| `convoys:write` | ✓ | ✓ | | | |
| `boxes:read` | ✓ | ✓ | ✓ | ✓ | |
| `boxes:write` | ✓ | ✓ | ✓ | | |
| `boxes:validate` | ✓ | | ✓ | | |
| `manifests:read` | ✓ | ✓ | ✓ | ✓ | |
| `manifests:write` | ✓ | ✓ | | | |
| `manifests:approve` | ✓ | | | | |
| `receivers:read` | ✓ | ✓ | ✓ | ✓ | ✓ |
| `receivers:write` | ✓ | | | | ✓ |
| `receivers:detail` | | | | | ✓ |

Three rows are worth understanding rather than memorising:

- **`manifests:approve` is Administrator-only** and separate from `manifests:write`, because
  approval is not another edit — it releases the Goods Movement Reference to HMRC and freezes the
  manifest permanently. The person who builds a manifest is not the person who signs it off.
- **`boxes:validate` is separate from `boxes:write`** for the same kind of reason: packing a box
  and vouching for what is in it are different acts, and the Loader is the one who opens it.
- **`receivers:detail` is the narrowest policy in the API** and the only one an Administrator is
  excluded from. `DELETE /receivers/{ref}` sits behind it too, because removing a receiver removes
  its address. See [`gotchas-and-open-questions.md`](gotchas-and-open-questions.md) §3.

---

## A worked example

Reading a delivery address — the one path that needs a specific login and leaves an audit trail:

```bash
GO=$(curl -s -X POST http://localhost:8081/realms/freedom/protocol/openid-connect/token \
  -d grant_type=password -d client_id=freedom-app \
  -d client_secret=local-freedom-client-secret \
  -d username=groundofficer -d password=password -d scope=openid | jq -r .access_token)

REF=$(curl -s -i -X POST http://localhost:8080/receivers \
  -H "Authorization: Bearer $GO" -H 'Content-Type: application/json' \
  -d '{"organisation":"Kharkiv Regional Hospital","region":"Kharkiv oblast"}' \
  | grep -i '^location:' | tr -d '\r' | sed 's|.*/||')

curl -s -X PUT "http://localhost:8080/receivers/$REF/detail" \
  -H "Authorization: Bearer $GO" -H 'Content-Type: application/json' \
  -d '{"contactName":"Olena Kovalenko","contactPhone":"+380501234567",
       "addressLine1":"12 Vulytsia Sumska","city":"Kharkiv","postCode":"61002"}'

# Ground Officer: 200, and this read is written to sensitive.ReceiverDetailAccessLog
curl -s "http://localhost:8080/receivers/$REF/detail?reason=Delivery%20scheduled%2012%20Sept" \
  -H "Authorization: Bearer $GO"

# Anyone else: 403, and nothing is logged, because a refusal is not an access
OP=$(curl -s -X POST http://localhost:8081/realms/freedom/protocol/openid-connect/token \
  -d grant_type=password -d client_id=freedom-app \
  -d client_secret=local-freedom-client-secret \
  -d username=operator -d password=password -d scope=openid | jq -r .access_token)

curl -s -o /dev/null -w '%{http_code}\n' "http://localhost:8080/receivers/$REF/detail" \
  -H "Authorization: Bearer $OP"
```

The same thing in PowerShell, using the `Get-FreedomToken` function from above:

```powershell
$go = @{ Authorization = "Bearer $(Get-FreedomToken 'groundofficer')" }

$created = Invoke-WebRequest -Method Post -Uri 'http://localhost:8080/receivers' -Headers $go `
    -ContentType 'application/json' `
    -Body '{"organisation":"Kharkiv Regional Hospital","region":"Kharkiv oblast"}'
$ref = $created.Headers.Location -replace '.*/',''

Invoke-WebRequest -Method Put -Uri "http://localhost:8080/receivers/$ref/detail" -Headers $go `
    -ContentType 'application/json' `
    -Body '{"contactName":"Olena Kovalenko","contactPhone":"+380501234567",
            "addressLine1":"12 Vulytsia Sumska","city":"Kharkiv","postCode":"61002"}'

# Ground Officer: the full address, and the read is audited
Invoke-RestMethod -Headers $go `
    -Uri "http://localhost:8080/receivers/$ref/detail?reason=Delivery%20scheduled%2012%20Sept"

# Anyone else: 403
$op = @{ Authorization = "Bearer $(Get-FreedomToken 'operator')" }
(Invoke-WebRequest -Uri "http://localhost:8080/receivers/$ref/detail" -Headers $op `
    -SkipHttpErrorCheck).StatusCode
```

Note what `operator` gets from `GET /receivers/{ref}` — the organisation and region, and **no
address or contact fields at all**:

```json
{"ref":"f7e52ad1-…","organisation":"Kharkiv Regional Hospital","region":"Kharkiv oblast"}
```

That is not filtering. The two halves are different types, and the one the operational roles see
has nowhere to put a street or a contact. See
[`gotchas-and-open-questions.md`](gotchas-and-open-questions.md) §3.

---

## Running the API outside Docker

`dotnet run` starts the API on `http://localhost:5100`, but `appsettings.Development.json` carries
no OIDC configuration — the container gets it from compose. Supply it yourself:

```bash
export ConnectionStrings__Freedom='Server=localhost,1433;Database=Freedom;User Id=freedom_app;Password=Local_Freedom_App_1;TrustServerCertificate=True;Encrypt=False'
export Oidc__Authority='http://localhost:8081/realms/freedom'
export Oidc__RequireHttpsMetadata=false
export Hosting__UseHttpsRedirection=false

dotnet run --project src/UA.Action.Freedom.Api/UA.Action.Freedom.Api.csproj
```

No `Oidc__MetadataAddress` is needed here — see the next section for why the container needs one
and you do not.

Keycloak still has to be running for token validation to work. Everything else (SQL, Azurite)
degrades gracefully: the app starts, and `/health/ready` explains what is missing.

---

## Troubleshooting

**`401` on every request, and the logs say the token could not be validated.**
Most often the issuer does not match. The token's `iss` is `http://localhost:8081/realms/freedom`
and `Oidc:Authority` must be exactly that — including the port, and with no trailing slash.

**`403` when you expected `200`.**
Check the `roles` claim actually contains what you think (decode the token, above). If it is empty
the protocol mapper is missing, which means `tofu apply` did not complete — run it again. If the
claim is populated but policies still fail, `MapInboundClaims` has been turned back on somewhere.

**`invalid_client` or `unauthorized_client` from the token endpoint.**
The realm is not provisioned. Run `tofu apply` in `iac/tofu`.

**`invalid_grant` / `Account is not fully set up`.**
The seed user was created but the password did not take. `tofu apply` again, or reset it in the
Keycloak admin console at <http://localhost:8081> (`admin` / `admin`, from `.env`).

**Everything worked yesterday and now the container cannot fetch signing keys.**
This is the split-horizon issue. The app container resolves `keycloak:8080` over the compose
network, while your browser and the token's `iss` use `localhost:8081`. That is why
`Oidc__MetadataAddress` points at `keycloak:8080` while `Oidc__Authority` points at
`localhost:8081`, and why Keycloak runs with `KC_HOSTNAME_BACKCHANNEL_DYNAMIC: "true"`. Remove any
one of those three and in-container validation breaks while everything looks right from the host.

**The BDD suite skips every scenario.**
It talks to the deployed containers, not your working tree. Rebuild the image first:
`docker compose build app manifest-worker && docker compose up -d --wait app edge manifest-worker`.

---

## How the tests do it

Worth knowing, because it tells you what to imitate:

- **Component tests** (`tests/UA.Action.Freedom.Tests.Component`) do not use Keycloak at all. They
  swap in `TestAuthHandler`, which mints a principal with whatever roles the test names —
  `FreedomApi.WithVehicles(repository, roles: "Loader")`. It builds the identity with the same
  `roles` claim type as production, so the policies under test are the real ones.
- **BDD tests** (`tests/UA.Action.Freedom.Tests.BDD`) use the real thing: password grant against
  the running Keycloak, cached per scenario, written as
  `Given I am authenticated as "groundofficer"`. Targets are overridable with `FREEDOM_BASE_URL`,
  `FREEDOM_OIDC_URL`, `FREEDOM_OIDC_CLIENT_ID`, `FREEDOM_OIDC_CLIENT_SECRET` and
  `FREEDOM_TEST_PASSWORD`.

If you are adding a slice, both are part of it: the component test proves the policy split, and
the BDD scenario proves it against a real token.

---

## What changes in Azure

The password grant is a local convenience and does not survive the move. Entra External ID uses
authorization code with PKCE for users, and client credentials for service-to-service. The
operator UI already uses Authorization Code + PKCE (against `freedom-spa`), so for it only the
authority and audience change. What does survive unchanged:

- the flat `roles` claim, and every policy written against it;
- `RoleClaimType` / `MapInboundClaims`;
- the role names themselves.

What changes: `Oidc:Authority` becomes the Entra tenant, `Oidc:Audience` gets set (so audience
validation switches on), `RequireHttpsMetadata` returns to `true`, and the client secret is
replaced by a federated credential. See [`recommendations.md`](recommendations.md) §4.7.

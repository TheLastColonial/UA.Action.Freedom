# Local environment

A working stand-in for the Azure deployment in [`docs/c4/2-containers.puml`](../docs/c4/2-containers.puml),
running entirely on your machine. It exists so the Freedom Application, the Customs Worker
and the plumbing between them can be built and tested against real backing services long
before an Azure subscription exists.

Two commands:

```bash
cd iac/local && cp .env.example .env && docker compose up -d --wait
cd ../tofu   && tofu init && tofu apply
```

Then:

```bash
curl http://localhost:8080/health/ready
```

Every check `Healthy` means the environment is wired up correctly.

---

## What it is

**docker compose is the substrate** — it starts the containers that play the part of an
Azure region. It deliberately creates nothing inside them.

**OpenTofu is the control plane** — it creates the blob containers, queues, database schema,
realm, client, app roles, groups and users, the same way `azurerm` and `azuread` would in Azure.

That split is the point. `docker compose down` is like tearing down a region;
`tofu destroy` is like deleting a resource group. Keeping resource creation in OpenTofu
means the local environment is provisioned by the same kind of artefact the real one will
be, rather than by a pile of init scripts that teach you nothing.

| C4 container | Stand-in | Licence |
| --- | --- | --- |
| Edge (Cloudflare Free) | Traefik | MIT |
| Freedom Application (Container Apps) | `src/UA.Action.Freedom.Api` | — |
| Customs Worker (Azure Functions) | `src/UA.Action.Freedom.CustomsWorker` | — |
| Document Store (Blob Storage) | Azurite | MIT |
| Customs Work Queue (Queue Storage) | Azurite | MIT |
| Freedom Database (Azure SQL) | SQL Server 2022 | Developer EULA — see below |
| Identity Provider (Entra External ID) | Keycloak | Apache-2.0 |
| Telemetry (Application Insights) | Grafana OTEL-LGTM | Apache-2.0 / AGPL |
| Email / SMS (Communication Services) | Mailpit | MIT |
| Public Website (Static Web Apps) | nginx + a placeholder page | BSD-2 |
| HMRC GVMS + Push Pull Notifications | WireMock | Apache-2.0 |
| Secret Store (Key Vault) | *not simulated* — `.env` | — |

Everything is open source except SQL Server, which is a deliberate exception: it is the same
engine as Azure SQL, so T-SQL and the `sensitive` schema segregation
([`recommendations.md` §4.4](../docs/recommendations.md)) behave identically. PostgreSQL
would have been open source and wrong.

---

## Prerequisites

| Tool | Why |
| --- | --- |
| Docker (with Compose v2) | Everything runs in containers |
| [OpenTofu](https://opentofu.org) ≥ 1.9 | The control plane |
| [Azure CLI](https://learn.microsoft.com/cli/azure) | OpenTofu creates blob containers and queues through it |

Around **4 GB of free RAM** — SQL Server alone reserves about 2 GB.

If you would rather not install the Azure CLI, `iac/tofu/storage.tf` is the only place it is
used and `docker run --rm --network host mcr.microsoft.com/azure-cli az ...` is a drop-in
replacement.

---

## Running it

```bash
cd iac/local
cp .env.example .env          # nothing in here is a real secret
docker compose up -d --wait   # ~2 minutes on a cold start, mostly pulling images
```

`--wait` blocks until every container reports healthy. If it returns, the substrate is up.

```bash
cd ../tofu
tofu init                     # first time only
tofu apply
```

`tofu apply` prints an `environment` output with every URL. Re-running it is a no-op.

### Where everything is

| | |
| --- | --- |
| Freedom Application | <http://localhost:8080> |
| Readiness (start here) | <http://localhost:8080/health/ready> |
| API reference (Scalar) | <http://localhost:8080/scalar/v1> |
| Public website | <http://localhost:8080/site> |
| Identity (Keycloak) | <http://localhost:8081> — admin `admin` / `admin` |
| HMRC stubs (WireMock admin) | <http://localhost:8082/__admin/mappings> |
| Telemetry (Grafana) | <http://localhost:3000> — dashboard at <http://localhost:3000/d/freedom-dotnet> |
| Email inbox (Mailpit) | <http://localhost:8025> |
| Edge dashboard (Traefik) | <http://localhost:8090/dashboard/> |
| Azurite | blob `:10000`, queue `:10001`, table `:10002` |

Two workers run alongside the app, both queue-driven and neither listening on a port:
`freedom-customs-worker` drains `customs-work` and talks to HMRC (WireMock), and
`freedom-manifest-worker` drains `manifest-documents` and writes the document that travels with a
vehicle into the `manifests` container. To see one for yourself, approve a manifest and then read
the blob it produced:

```
docker compose logs manifest-worker --tail 5
az storage blob download --container-name manifests --name <MANIFEST-ID>.txt --file -
```

The document shows cargo, weights and a **region** — never a street address or a contact. The
Manifest Worker has no database access at all, which is what makes that structural rather than a
convention: the application composes the document and queues it, so the worker could not read a
delivery address even if it tried.
| SQL Server | `localhost:1433` — three logins, all in `.env`; see below |

**The application does not connect as `sa`.** `sa` is sysadmin and bypasses permission checks, which
would make the `DENY` on the `sensitive` schema — the control `docs/recommendations.md` §4.4 calls
load-bearing — decorative. Three logins exist instead:

| Login | Role | Can read |
| --- | --- | --- |
| `sa` | sysadmin | everything; used only to apply the schema bootstrap |
| `freedom_app` | `freedom_app` | full DML on `dbo`. **`DENY SELECT` on `sensitive`** — this is the application's own identity |
| `freedom_sensitive` | `ground_officer` | `sensitive` as well; the only way to resolve a Ukrainian delivery address |

The application is given both as `ConnectionStrings__Freedom` and `ConnectionStrings__FreedomSensitive`,
and only `ReceiverDetailRepository` takes the second. In Azure both become managed identities with
Entra-only authentication and no password at all (§4.2); only the connection string differs.

You can see the segregation for yourself:

```
docker exec freedom-mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U freedom_app   -P "$FREEDOM_APP_DB_PASSWORD" -C -d Freedom -Q "SELECT COUNT(1) FROM sensitive.ReceiverDetail"
# Msg 229 ... The SELECT permission was denied on the object 'ReceiverDetail'
```

Ports are all overridable in `.env` if something on your machine already owns one.

### Signing in

OpenTofu seeds three users, all with the password `password`:

| Username | Roles |
| --- | --- |
| `admin` | Administrator |
| `operator` | Dispatcher, Loader, Purchaser |
| `groundofficer` | GroundOfficer |

Each role is a Keycloak group of the same name, and each user is a member of the groups its
roles name. `groundofficer` is kept separate so the receiver-address segregation that role
carries in production holds locally too — no other seed user can resolve a receiver address.
Roles arrive in the token as a flat `roles` claim, which is the shape Entra app roles arrive
in — so authorisation policies written against this realm port across unchanged. To get a
token without a browser:

```bash
curl -s -X POST http://localhost:8081/realms/freedom/protocol/openid-connect/token \
  -d grant_type=password \
  -d client_id=freedom-app \
  -d client_secret=local-freedom-client-secret \
  -d username=operator -d password=password
```

### Exercising the customs path

Put a GMR submission on the queue and watch the worker pick it up:

```bash
export AZURE_STORAGE_CONNECTION_STRING="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1"

az storage message put --queue-name customs-work --content '{
  "manifestId": "MAN-0001",
  "haulierEori": "GB123456789000",
  "vehicleRegistration": "AB12CDE",
  "routeId": "20000",
  "localDateTimeOfDeparture": "2026-09-01T18:30"
}'

docker logs -f freedom-customs-worker
curl -s "http://localhost:8082/__admin/requests?limit=5"   # what HMRC was asked
```

### Changing an HMRC stub

Stubs are plain JSON in `local/wiremock/mappings/`, loaded at boot — there is no
provisioning step for them. Add a file, then:

```bash
docker compose restart wiremock
curl -X POST http://localhost:8082/__admin/scenarios/reset   # replay the outcome scenario
```

The notification stubs form a scenario: the first poll returns one pending GMR outcome,
acknowledging it empties the box. That is deliberate — it makes the polling loop terminate
the way it does against the real API instead of spinning on the same message forever.

### Changing the Grafana dashboard

`local/grafana/dashboards/freedom-dotnet.json` is provisioned into Grafana on every
container start — a `.NET Runtime & HTTP` dashboard in a `Freedom` folder, capturing the key
metrics from the application's OpenTelemetry instrumentation (HTTP server RED, outbound
dependency calls, the .NET runtime, Kestrel) plus a service logs panel and a recent-traces
table. It is parameterised by a `Service` variable so it also covers the Customs Worker once
that is instrumented.

The JSON file is the source of truth. Edit it, then either wait ~30s (Grafana re-reads on an
interval) or `docker compose restart telemetry`. UI edits are **not** persisted — Grafana
overwrites them from the file. To iterate in the UI, make the changes there, then export via
*Dashboard settings → JSON Model* and paste back into the file. See
`local/grafana/README.md`.

### Tearing down

```bash
cd iac/local && docker compose down -v   # -v also drops the data volumes
cd ../tofu   && rm -rf .terraform terraform.tfstate*
```

`docker compose down -v` is the real teardown: Azurite, SQL Server and Keycloak all keep
their state in docker volumes. There are deliberately no destroy-time provisioners in the
OpenTofu configuration, because they would fail whenever the volume had already gone —
which is most of the time.

---

## Known issues

### The Customs Worker cannot read HMRC notifications

**The outcome-collection half of the worker does not work, and the cause is a bug in the
committed SDK rather than in this environment.**

`HMRC.PushPullNotifications` cannot deserialise a notification. HMRC sends
`"messageContentType": "application/json"`; NSwag generated the enum with
`[EnumMember(Value = "application/json")]` but decorated the property with
`JsonStringEnumConverter<MessageContentType>`, which matches C# member names
(`Application_json`) and ignores `[EnumMember]` entirely. Every response fails with:

```
The JSON value could not be converted to HMRC.PushPullNotifications.MessageContentType.
Path: $[0].messageContentType
```

This would fail against real HMRC in exactly the same way — the stub is faithful, and
"fixing" the stub to emit `Application_json` would hide a production bug. The fix belongs in
`build/nswag/` and a regeneration, which is a separate piece of work.

Until then the worker logs a stack trace every `Worker__OutcomePollSeconds`. Raise that
value in `docker-compose.yml` if the noise gets in the way.

The submission half is unaffected and works end to end: queue → worker → HMRC → 202.

### `NU1903` on `System.Security.Cryptography.Xml`

`Azure.Extensions.AspNetCore.DataProtection.Blobs` brings in an 8.0.2 reference with
high-severity advisories. The assembly is pruned by .NET 10's framework and never reaches
the output directory — verified — so there is no actual exposure. Adding an explicit
reference makes it worse, not better, because a direct reference is not pruned.

---

## Things that will cost you an hour if you don't know them

**The Keycloak issuer is two different URLs.** Your browser reaches Keycloak on
`localhost:8081`; the application container reaches it on the compose network as
`keycloak:8080`. The issuer in a token must stay browser-facing or validation fails, so the
app is given `Oidc__Authority` (localhost, used for validation) *and* `Oidc__MetadataAddress`
(compose network, used to fetch discovery). Changing one without the other breaks sign-in in
a way the error message does not explain.

The same split bites token *validation* one level deeper: the discovery document names a
`jwks_uri` and `token_endpoint`, and by default Keycloak stamps those with `KC_HOSTNAME`
(`localhost:8081`), which the app container cannot reach — so every bearer token fails
signature validation with no useful log line. `KC_HOSTNAME_BACKCHANNEL_DYNAMIC: "true"` on
the `keycloak` service makes those two URLs follow the host the request came in on, so the
container gets `keycloak:8080` for the backchannel while `issuer` stays `localhost:8081`.

**The edge health checks liveness, not readiness.** `/health/ready` is red until
`tofu apply` has run, so pointing Traefik at it would deadlock the bootstrap — compose would
refuse to come up until provisioning had run, and provisioning runs after compose. The
consequence: between `compose up` and `tofu apply` the edge forwards traffic to an
application that cannot reach anything. `/health/ready` is where you find that out.

**`UseHttpsRedirection` must be off behind the edge.** Traefik (locally) and Cloudflare
(in Azure) terminate TLS and forward plain HTTP. Left on, the app redirects to HTTPS, the
redirect arrives back at the origin as HTTP, and it loops. `Hosting__UseHttpsRedirection=false`
in `docker-compose.yml` is what prevents that.

**Azure SDK retries are bounded on purpose.** The data-protection key ring is read from blob
storage during startup, so the SDK's default retry budget is also the cold-start budget — an
unreachable storage account stalled startup for 40 seconds before this was tuned down. With
`minReplicas: 0` a cold start is the normal case, not the exception. See
`StorageExtensions.ConfigureRetry`.

**OpenTofu's provisioners run in sequence deliberately.** Every endpoint here is reached
through Docker Desktop's loopback proxy, which resets connections when the whole graph hits
it at once. The `depends_on` chain in `storage.tf` and `database.tf` is not describing a
real dependency; it is working around that.

---

## Troubleshooting

**`container freedom-edge is unhealthy`** — usually a port clash on 8080. Change
`EDGE_HTTP_PORT` in `.env`.

**SQL Server exits or never becomes healthy** — it needs ~2 GB. Check Docker Desktop's
memory allocation, and check the `MSSQL_SA_PASSWORD` in `.env` still satisfies SQL Server's
complexity rules (8+ characters, three of upper / lower / digit / symbol).

**`az` fails with "Connection string is either blank or malformed"** — you are passing it on
a command line on Windows, where `cmd /C` eats the quoting. Use
`AZURE_STORAGE_CONNECTION_STRING` in the environment instead, as `storage.tf` does.

**`/health/ready` says a container or queue "does not exist"** — `tofu apply` has not run, or
you ran `docker compose down -v` afterwards, which wipes the Azurite volume. Re-run
`tofu apply`; the triggers are keyed on the endpoint so it will recreate them.

**Sign-in fails with an issuer mismatch** — see "The Keycloak issuer is two different URLs".

---

## What this environment is not

Worth being explicit, because the gaps are where local-passing and Azure-failing diverge:

1. **No managed identity.** Everything here authenticates with connection strings and shared
   keys. Azure uses managed identity with shared-key authorisation disabled entirely
   ([§4.2](../docs/recommendations.md)). Credential acquisition is isolated in the
   composition root, so that is the only file that differs — but it is untested here.
2. **The worker is a `BackgroundService`, not an Azure Functions host.** It models the same
   queue-triggered and timer-triggered behaviour. Moving to Functions replaces
   `CustomsWorkerService.cs` and nothing else.
3. **No Key Vault.** Secrets are `.env` values.
4. **Traefik is not Cloudflare.** Managed WAF rules, Turnstile and the origin lock
   ([§4.6](../docs/recommendations.md)) cannot be rehearsed here.
5. **No auto-pause.** The Azure SQL free offer's 60-minute minimum auto-pause delay
   ([§2.3](../docs/recommendations.md)) is the sharpest edge in the cost model and has no
   local equivalent. Nothing you do here will warn you about it.

---

## Layout

```
iac/
  local/
    docker-compose.yml           the substrate
    .env.example                 copy to .env
    traefik/dynamic/routes.yml   edge routing
    wiremock/mappings/           HMRC stubs, loaded at boot
    sql/001-schemas.sql          schemas, roles and the sensitive/ segregation
    website/html/                Static Web Apps placeholder
    grafana/                     Grafana dashboards, provisioned at boot
  tofu/
    versions.tf variables.tf outputs.tf
    keycloak.tf                  realm, client, app roles, role groups, seeded users
    storage.tf                   blob containers and queues
    database.tf                  applies sql/001-schemas.sql
```

Dockerfiles live next to their projects — `src/UA.Action.Freedom.Api/Dockerfile` and
`src/UA.Action.Freedom.CustomsWorker/Dockerfile` — and build from the repository root.

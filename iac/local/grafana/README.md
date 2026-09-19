# Grafana provisioning

Mounted into the `telemetry` container (`grafana/otel-lgtm`, pinned) by `../docker-compose.yml`.
Grafana loads everything under `provisioning/` on start, alongside the image's own bundled
providers.

```
grafana/
  provisioning/
    dashboards/
      freedom.yaml            a file provider pointing at /var/lib/grafana/dashboards
  dashboards/
    freedom-dotnet.json       ".NET Runtime & HTTP"
    freedom-api.json          "API"
    freedom-customs-worker.json
    freedom-manifest-worker.json
    freedom-manifest-pipeline.json
    freedom-convoy-operations.json
    freedom-access-security.json
```

All three services — `freedom-app`, `freedom-customs-worker`, `freedom-manifest-worker` — emit through
the shared `src/UA.Action.Freedom.Telemetry` wiring. Datasource UIDs are the fixed ones the
otel-lgtm image provisions: `prometheus`, `loki`, `tempo`. Every dashboard's `Service`/`job`
filter relies on `job = service.name`, which holds only while **`service.namespace` is unset**.

## The dashboards

Folder **`Freedom`**, at `http://localhost:3000/d/<uid>`.

| uid | For |
| --- | --- |
| `freedom-dotnet` | HTTP server RED, outbound calls, .NET runtime, Kestrel, logs and traces. A `Service` variable scopes it to any of the three services. Health probes excluded. |
| `freedom-api` | Requests by route, 4xx/5xx mix, slowest routes, authentication and authorization, command outcomes per handler, SQL errors, storage/Keycloak dependency latency, error logs and traces. |
| `freedom-customs-worker` | Queue depth and oldest age, message dispositions, dead-letter reasons by HMRC status, submission duration, outcome notifications by GMR state, loop heartbeats. |
| `freedom-manifest-worker` | Queue depth and age, dispositions, render and store duration, document size, blob latency, loop heartbeat. |
| `freedom-manifest-pipeline` | An approval end to end: transitions, partial failures, both queues, poison depth, GMR states, cross-service traces. |
| `freedom-convoy-operations` | Convoy, box, bay, vehicle and volunteer command outcomes. |
| `freedom-access-security` | Aggregate address lookups and 401/403 rates. Never a principal or a reference. |

## The metric catalogue

OpenTelemetry names become Prometheus names by replacing dots with underscores, appending the unit
(`_seconds`, `_bytes`) and `_total` for counters; attribute dots become underscores too
(`http.response.status_code` → `http_response_status_code`). Histograms are classic
`_bucket` / `_count` / `_sum`. Metrics are exported every 60 s.

| Prometheus name | Type | Labels | Emitted by |
| --- | --- | --- | --- |
| `freedom_handler_invocations_total` | counter | `handler`, `outcome`, `result` (`ok` \| `rejected` \| `error` \| `cancelled`) | app — every command handler |
| `freedom_handler_duration_seconds` | histogram | `handler`, `result` | app |
| `freedom_manifest_transitions_total` | counter | `from`, `to`, `outcome` | app |
| `freedom_manifest_approve_partial_failures_total` | counter | `stage` (`gmr` \| `document`) | app — froze a manifest, then could not hand its paperwork on |
| `freedom_receiver_detail_resolves_total` | counter | `result` (`found` \| `not_found`) | app — aggregate only |
| `freedom_db_errors_total` | counter | `sql_error` (`deadlock` \| `timeout` \| `unavailable` \| `foreign_key` \| `unique` \| `other`) | app — unhandled SQL errors only |
| `freedom_queue_enqueue_total` | counter | `queue`, `result` (`ok` \| `failed`) | app |
| `freedom_queue_messages_processed_total` | counter | `queue`, `outcome` (`completed` \| `dead_lettered` \| `left_for_retry`) | workers |
| `freedom_queue_message_age_seconds` | histogram | `queue` | workers — wait before pickup |
| `freedom_queue_redeliveries_total` | counter | `queue` | workers |
| `freedom_queue_depth` | gauge | `queue`, `kind` (`work` \| `poison`) | workers — absent when the queue cannot be read |
| `freedom_queue_oldest_message_age_seconds` | gauge | `queue`, `kind` | workers |
| `freedom_worker_loop_last_success_seconds` | gauge (unix time) | `loop` | workers — `time() - x` is staleness |
| `freedom_worker_loop_errors_total` | counter | `loop` | workers |
| `freedom_gmr_submission_duration_seconds` | histogram | `outcome` (`accepted` \| `rejected` \| `error`) | customs worker |
| `freedom_gmr_dead_letters_total` | counter | `reason`, `http_response_status_code` (for `hmrc_rejected`) | customs worker |
| `freedom_gmr_outcome_notifications_total` | counter | `result`, `state` (GMR state, or `none` / `unknown`) | customs worker |
| `freedom_manifest_document_render_duration_seconds` | histogram | — | manifest worker |
| `freedom_manifest_document_store_duration_seconds` | histogram | `result` (`ok` \| `error`) | manifest worker |
| `freedom_manifest_document_lines` | histogram | — | manifest worker — boxes per document |

`queue` is the *logical* name (`customs-work`, `manifest-documents`), not the storage queue name.
Also available from the runtime: `http_server_request_duration_seconds_*` (health probes excluded),
`http_client_request_duration_seconds_*` (by `server_address`), `aspnetcore_authentication_*`,
`aspnetcore_authorization_attempts_total`, `dotnet_*`, `kestrel_*`.

**Never a label:** a person, receiver, plate, VIN, EORI, manifest or GMR reference, principal id or
free text. Those belong on a span or in a log line, where the redaction rules apply.

Logs are in Loki (`{service_name="freedom-customs-worker"}`, labels `service_name`,
`service_instance_id`); traces in Tempo (`{ resource.service.name = "freedom-app" }`). An approval's
trace does **not** continue into the workers: the worker's `process <queue>` span (kind `consumer`)
has a **link** to the `publish <queue>` span that queued the message.

## Editing

The JSON files are authoritative. Grafana re-reads them every 30s and on container start, and
`allowUiUpdates: false` means UI edits are not saved back — Grafana will overwrite them.

To change a dashboard:

1. Edit `dashboards/<uid>.json` directly, or edit in the UI and copy the model out
   via *Dashboard settings → JSON Model*.
2. `docker compose restart telemetry` (or wait ~30s).
3. `dotnet test --project tests/UA.Action.Freedom.Tests.Unit/UA.Action.Freedom.Tests.Unit.csproj --filter-class "*DashboardFileTests"`

Keep `uid`, `title` and the file name stable across edits — the deep link and the provider's
change detection key on them, and `DashboardFileTests` requires the uid to match the file name.

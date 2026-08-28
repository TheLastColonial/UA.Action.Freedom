# Grafana provisioning

Mounted into the `telemetry` container (`grafana/otel-lgtm`) by `../docker-compose.yml`.
Grafana loads everything under `provisioning/` on start, alongside the image's own bundled
providers.

```
grafana/
  provisioning/
    dashboards/
      freedom.yaml            a file provider pointing at /var/lib/grafana/dashboards
  dashboards/
    freedom-dotnet.json       ".NET Runtime & HTTP" — mounted at that path
```

## The dashboard

`freedom-dotnet.json` — **`.NET Runtime & HTTP`**, folder **`Freedom`**, uid `freedom-dotnet`
(<http://localhost:3000/d/freedom-dotnet>). It reads the metrics the Freedom Application
emits through its OpenTelemetry wiring (`src/UA.Action.Freedom.Api/Installer/TelemetryInstaller.cs`):

| Section | Panels |
| --- | --- |
| HTTP Server — RED | request rate by route, duration p50/p95/p99, throughput, 5xx error rate, p95, in-flight, status-code mix, duration heatmap |
| HTTP Client — outbound | request rate / p95 latency / errors by peer (`server_address` — Azurite, Keycloak, WireMock), active requests & open connections |
| .NET Runtime | CPU cores used vs. available, working set / GC heap / committed memory, GC heap by generation, collections & pause time, allocation rate, exceptions, thread-pool threads / work items / queue length, lock contention |
| Kestrel | active & queued connections, connection duration p95 |
| Logs & Traces | Loki logs for the service; a recent-traces table from Tempo |

A **`Service`** template variable (`label_values(dotnet_process_memory_working_set_bytes, job)`,
default `freedom-app`) scopes every query, so the same dashboard covers
`freedom-customs-worker` once that gets the same instrumentation.

Datasource UIDs are the fixed ones the otel-lgtm image provisions: `prometheus`, `loki`,
`tempo`.

## Editing

The JSON file is authoritative. Grafana re-reads it every 30s and on container start, and
`allowUiUpdates: false` means UI edits are not saved back — Grafana will overwrite them.

To change the dashboard:

1. Edit `dashboards/freedom-dotnet.json` directly, or edit in the UI and copy the model out
   via *Dashboard settings → JSON Model*.
2. `docker compose restart telemetry` (or wait ~30s).

Keep `uid`, `title` and the file name stable across edits — the deep link and the provider's
change detection key on them.

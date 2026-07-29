# MSOSync Grafana Dashboards

Four dashboards for monitoring MSOSync with Prometheus + Grafana 10+.

## Prerequisites

- Grafana 10.0+ with a Prometheus datasource configured
- MSOSync running with `Telemetry:Enabled = true` (see `appsettings.json`)
- Prometheus scraping MSOSync `/metrics` endpoint

## Dashboards

| File | UID | Description |
|------|-----|-------------|
| `msosync-overview.json` | `msosync-overview` | Batches sent, error rate, throughput |
| `msosync-pipeline.json` | `msosync-pipeline` | Per-stage latency histograms (P50/P95/P99) |
| `msosync-nodes.json` | `msosync-nodes` | Per-node send latency and health |
| `msosync-slo.json` | `msosync-slo` | Delivery rate and P99 latency vs SLO targets |

## Import

1. Open Grafana → Dashboards → Import
2. Upload the JSON file or paste its contents
3. Select your Prometheus datasource when prompted
4. Click Import

## Metric Names

These dashboards use metrics emitted by `OtelMetricsService`:

- `sync_batches_sent_total` — counter
- `sync_pipeline_fetch_ms` — histogram (ms)
- `sync_pipeline_compress_ms` — histogram (ms)
- `sync_pipeline_send_ms` — histogram (ms)
- `sync_pipeline_ack_ms` — histogram (ms)

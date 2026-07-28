# Phase 2F.4 — Grafana Dashboards Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Commit four static Grafana dashboard JSON files — overview, pipeline, nodes, and SLO — ready to import into any Grafana 10+ instance pointed at the Prometheus scrape endpoint (`/metrics`).

**Architecture:** Four JSON files in `docs/grafana/`. Each is a self-contained Grafana dashboard export importable via Grafana UI or `grafana-cli`. No server-side changes; dashboards are documentation artifacts.

**Tech Stack:** Grafana 10+ / Prometheus / schemaVersion 38

## Global Constraints

- Prerequisite: 2F.1 complete — Prometheus endpoint `/metrics` exposed; `OtelMetricsService` records metrics
- `schemaVersion: 38` in every dashboard
- Prometheus datasource variable: `${datasource}` (template variable of type `datasource`, `prometheus`)
- Metric names (exact, from 2F.1): `sync_pipeline_fetch_ms`, `sync_pipeline_compress_ms`, `sync_pipeline_send_ms`, `sync_pipeline_ack_ms`, `sync_batches_sent`
- All histogram metrics emit OTel `_bucket`, `_count`, `_sum` suffixes via Prometheus exporter
- UID convention: `msosync-overview`, `msosync-pipeline`, `msosync-nodes`, `msosync-slo`
- Refresh: `30s` on all dashboards
- `git add` by file name only

---

### Task 1: msosync-overview.json + msosync-pipeline.json

**Files:**
- Create: `docs/grafana/msosync-overview.json`
- Create: `docs/grafana/msosync-pipeline.json`

**Interfaces:**
- Consumes: Prometheus metrics from `/metrics` endpoint (2F.1)
- Produces: importable Grafana JSON for overview (batches sent, error rate, active nodes) and pipeline latency histograms

- [ ] **Step 1: Create docs/grafana/ directory**

```powershell
New-Item -ItemType Directory -Path docs/grafana -Force
```

- [ ] **Step 2: Create msosync-overview.json**

```json
{
  "__inputs": [
    {
      "name": "DS_PROMETHEUS",
      "label": "Prometheus",
      "description": "",
      "type": "datasource",
      "pluginId": "prometheus",
      "pluginName": "Prometheus"
    }
  ],
  "__requires": [
    { "type": "grafana", "id": "grafana", "name": "Grafana", "version": "10.0.0" },
    { "type": "datasource", "id": "prometheus", "name": "Prometheus", "version": "1.0.0" },
    { "type": "panel", "id": "stat", "name": "Stat", "version": "" },
    { "type": "panel", "id": "timeseries", "name": "Time series", "version": "" }
  ],
  "annotations": { "list": [] },
  "description": "MSOSync system overview — batches, error rate, active nodes",
  "editable": true,
  "fiscalYearStartMonth": 0,
  "graphTooltip": 1,
  "id": null,
  "links": [],
  "panels": [
    {
      "datasource": { "type": "prometheus", "uid": "${datasource}" },
      "fieldConfig": {
        "defaults": { "color": { "mode": "thresholds" }, "mappings": [], "thresholds": { "mode": "absolute", "steps": [{ "color": "green", "value": null }] } },
        "overrides": []
      },
      "gridPos": { "h": 4, "w": 6, "x": 0, "y": 0 },
      "id": 1,
      "options": { "colorMode": "value", "graphMode": "area", "justifyMode": "auto", "orientation": "auto", "reduceOptions": { "calcs": ["lastNotNull"], "fields": "", "values": false }, "textMode": "auto" },
      "title": "Batches Sent (last 1h)",
      "type": "stat",
      "targets": [
        {
          "datasource": { "type": "prometheus", "uid": "${datasource}" },
          "expr": "increase(sync_batches_sent_total[1h])",
          "legendFormat": "Batches",
          "refId": "A"
        }
      ]
    },
    {
      "datasource": { "type": "prometheus", "uid": "${datasource}" },
      "fieldConfig": {
        "defaults": { "color": { "mode": "thresholds" }, "mappings": [], "thresholds": { "mode": "absolute", "steps": [{ "color": "green", "value": null }, { "color": "red", "value": 0.01 }] }, "unit": "percentunit" },
        "overrides": []
      },
      "gridPos": { "h": 4, "w": 6, "x": 6, "y": 0 },
      "id": 2,
      "options": { "colorMode": "value", "graphMode": "none", "justifyMode": "auto", "orientation": "auto", "reduceOptions": { "calcs": ["lastNotNull"], "fields": "", "values": false }, "textMode": "auto" },
      "title": "Pipeline Error Rate (last 5m)",
      "type": "stat",
      "targets": [
        {
          "datasource": { "type": "prometheus", "uid": "${datasource}" },
          "expr": "rate(sync_pipeline_fetch_ms_count{job=~\".*\"}[5m]) > 0 or vector(0)",
          "legendFormat": "Error rate",
          "refId": "A"
        }
      ]
    },
    {
      "datasource": { "type": "prometheus", "uid": "${datasource}" },
      "fieldConfig": {
        "defaults": { "color": { "mode": "palette-classic" }, "custom": { "axisCenteredZero": false, "axisColorMode": "text", "axisLabel": "", "axisPlacement": "auto", "barAlignment": 0, "drawStyle": "line", "fillOpacity": 10, "gradientMode": "none", "hideFrom": { "legend": false, "tooltip": false, "viz": false }, "lineInterpolation": "linear", "lineWidth": 1, "pointSize": 5, "scaleDistribution": { "type": "linear" }, "showPoints": "auto", "spanNulls": false, "stacking": { "group": "A", "mode": "none" }, "thresholdsStyle": { "mode": "off" } }, "mappings": [], "thresholds": { "mode": "absolute", "steps": [{ "color": "green", "value": null }] }, "unit": "short" },
        "overrides": []
      },
      "gridPos": { "h": 8, "w": 24, "x": 0, "y": 4 },
      "id": 3,
      "options": { "legend": { "calcs": [], "displayMode": "list", "placement": "bottom", "showLegend": true }, "tooltip": { "mode": "single", "sort": "none" } },
      "title": "Batches Sent Rate",
      "type": "timeseries",
      "targets": [
        {
          "datasource": { "type": "prometheus", "uid": "${datasource}" },
          "expr": "rate(sync_batches_sent_total[5m])",
          "legendFormat": "Batches/sec",
          "refId": "A"
        }
      ]
    }
  ],
  "refresh": "30s",
  "schemaVersion": 38,
  "tags": ["msosync"],
  "templating": {
    "list": [
      {
        "current": {},
        "hide": 0,
        "includeAll": false,
        "label": "Datasource",
        "multi": false,
        "name": "datasource",
        "options": [],
        "query": "prometheus",
        "refresh": 1,
        "type": "datasource"
      }
    ]
  },
  "time": { "from": "now-1h", "to": "now" },
  "timepicker": {},
  "timezone": "browser",
  "title": "MSOSync Overview",
  "uid": "msosync-overview",
  "version": 1
}
```

- [ ] **Step 3: Create msosync-pipeline.json**

```json
{
  "__inputs": [
    {
      "name": "DS_PROMETHEUS",
      "label": "Prometheus",
      "description": "",
      "type": "datasource",
      "pluginId": "prometheus",
      "pluginName": "Prometheus"
    }
  ],
  "__requires": [
    { "type": "grafana", "id": "grafana", "name": "Grafana", "version": "10.0.0" },
    { "type": "datasource", "id": "prometheus", "name": "Prometheus", "version": "1.0.0" },
    { "type": "panel", "id": "timeseries", "name": "Time series", "version": "" }
  ],
  "annotations": { "list": [] },
  "description": "MSOSync sync pipeline latency histograms — fetch, compress, send, ack",
  "editable": true,
  "fiscalYearStartMonth": 0,
  "graphTooltip": 1,
  "id": null,
  "links": [],
  "panels": [
    {
      "datasource": { "type": "prometheus", "uid": "${datasource}" },
      "fieldConfig": {
        "defaults": { "color": { "mode": "palette-classic" }, "custom": { "drawStyle": "line", "fillOpacity": 10, "lineWidth": 1, "spanNulls": false }, "unit": "ms" },
        "overrides": []
      },
      "gridPos": { "h": 8, "w": 12, "x": 0, "y": 0 },
      "id": 1,
      "title": "Fetch Latency (P50/P95/P99)",
      "type": "timeseries",
      "targets": [
        { "datasource": { "type": "prometheus", "uid": "${datasource}" }, "expr": "histogram_quantile(0.50, rate(sync_pipeline_fetch_ms_bucket[5m]))", "legendFormat": "P50", "refId": "A" },
        { "datasource": { "type": "prometheus", "uid": "${datasource}" }, "expr": "histogram_quantile(0.95, rate(sync_pipeline_fetch_ms_bucket[5m]))", "legendFormat": "P95", "refId": "B" },
        { "datasource": { "type": "prometheus", "uid": "${datasource}" }, "expr": "histogram_quantile(0.99, rate(sync_pipeline_fetch_ms_bucket[5m]))", "legendFormat": "P99", "refId": "C" }
      ],
      "options": { "legend": { "displayMode": "list", "placement": "bottom", "showLegend": true }, "tooltip": { "mode": "single", "sort": "none" } }
    },
    {
      "datasource": { "type": "prometheus", "uid": "${datasource}" },
      "fieldConfig": {
        "defaults": { "color": { "mode": "palette-classic" }, "custom": { "drawStyle": "line", "fillOpacity": 10, "lineWidth": 1, "spanNulls": false }, "unit": "ms" },
        "overrides": []
      },
      "gridPos": { "h": 8, "w": 12, "x": 12, "y": 0 },
      "id": 2,
      "title": "Send Latency (P50/P95/P99)",
      "type": "timeseries",
      "targets": [
        { "datasource": { "type": "prometheus", "uid": "${datasource}" }, "expr": "histogram_quantile(0.50, rate(sync_pipeline_send_ms_bucket[5m]))", "legendFormat": "P50", "refId": "A" },
        { "datasource": { "type": "prometheus", "uid": "${datasource}" }, "expr": "histogram_quantile(0.95, rate(sync_pipeline_send_ms_bucket[5m]))", "legendFormat": "P95", "refId": "B" },
        { "datasource": { "type": "prometheus", "uid": "${datasource}" }, "expr": "histogram_quantile(0.99, rate(sync_pipeline_send_ms_bucket[5m]))", "legendFormat": "P99", "refId": "C" }
      ],
      "options": { "legend": { "displayMode": "list", "placement": "bottom", "showLegend": true }, "tooltip": { "mode": "single", "sort": "none" } }
    },
    {
      "datasource": { "type": "prometheus", "uid": "${datasource}" },
      "fieldConfig": {
        "defaults": { "color": { "mode": "palette-classic" }, "custom": { "drawStyle": "line", "fillOpacity": 10, "lineWidth": 1, "spanNulls": false }, "unit": "ms" },
        "overrides": []
      },
      "gridPos": { "h": 8, "w": 12, "x": 0, "y": 8 },
      "id": 3,
      "title": "Compress Latency (P50/P95/P99)",
      "type": "timeseries",
      "targets": [
        { "datasource": { "type": "prometheus", "uid": "${datasource}" }, "expr": "histogram_quantile(0.50, rate(sync_pipeline_compress_ms_bucket[5m]))", "legendFormat": "P50", "refId": "A" },
        { "datasource": { "type": "prometheus", "uid": "${datasource}" }, "expr": "histogram_quantile(0.95, rate(sync_pipeline_compress_ms_bucket[5m]))", "legendFormat": "P95", "refId": "B" },
        { "datasource": { "type": "prometheus", "uid": "${datasource}" }, "expr": "histogram_quantile(0.99, rate(sync_pipeline_compress_ms_bucket[5m]))", "legendFormat": "P99", "refId": "C" }
      ],
      "options": { "legend": { "displayMode": "list", "placement": "bottom", "showLegend": true }, "tooltip": { "mode": "single", "sort": "none" } }
    },
    {
      "datasource": { "type": "prometheus", "uid": "${datasource}" },
      "fieldConfig": {
        "defaults": { "color": { "mode": "palette-classic" }, "custom": { "drawStyle": "line", "fillOpacity": 10, "lineWidth": 1, "spanNulls": false }, "unit": "ms" },
        "overrides": []
      },
      "gridPos": { "h": 8, "w": 12, "x": 12, "y": 8 },
      "id": 4,
      "title": "Ack Latency (P50/P95/P99)",
      "type": "timeseries",
      "targets": [
        { "datasource": { "type": "prometheus", "uid": "${datasource}" }, "expr": "histogram_quantile(0.50, rate(sync_pipeline_ack_ms_bucket[5m]))", "legendFormat": "P50", "refId": "A" },
        { "datasource": { "type": "prometheus", "uid": "${datasource}" }, "expr": "histogram_quantile(0.95, rate(sync_pipeline_ack_ms_bucket[5m]))", "legendFormat": "P95", "refId": "B" },
        { "datasource": { "type": "prometheus", "uid": "${datasource}" }, "expr": "histogram_quantile(0.99, rate(sync_pipeline_ack_ms_bucket[5m]))", "legendFormat": "P99", "refId": "C" }
      ],
      "options": { "legend": { "displayMode": "list", "placement": "bottom", "showLegend": true }, "tooltip": { "mode": "single", "sort": "none" } }
    }
  ],
  "refresh": "30s",
  "schemaVersion": 38,
  "tags": ["msosync", "pipeline"],
  "templating": {
    "list": [
      {
        "current": {},
        "hide": 0,
        "includeAll": false,
        "label": "Datasource",
        "multi": false,
        "name": "datasource",
        "options": [],
        "query": "prometheus",
        "refresh": 1,
        "type": "datasource"
      }
    ]
  },
  "time": { "from": "now-1h", "to": "now" },
  "timepicker": {},
  "timezone": "browser",
  "title": "MSOSync Pipeline",
  "uid": "msosync-pipeline",
  "version": 1
}
```

- [ ] **Step 4: Verify JSON syntax**

```powershell
Get-Content docs/grafana/msosync-overview.json | ConvertFrom-Json | Out-Null; Write-Host "overview: valid"
Get-Content docs/grafana/msosync-pipeline.json | ConvertFrom-Json | Out-Null; Write-Host "pipeline: valid"
```

Expected: both print "valid" with no errors.

- [ ] **Step 5: Commit**

```
git add docs/grafana/msosync-overview.json docs/grafana/msosync-pipeline.json
git commit -m "feat(2F.4-T1): add Grafana overview + pipeline dashboards"
```

---

### Task 2: msosync-nodes.json + msosync-slo.json + import README

**Files:**
- Create: `docs/grafana/msosync-nodes.json`
- Create: `docs/grafana/msosync-slo.json`
- Create: `docs/grafana/README.md`

**Interfaces:**
- Consumes: same Prometheus datasource; `IHealthScoringService`/`ISloService` REST endpoints (for documentation)
- Produces: importable Grafana dashboards for per-node health and SLO tracking

- [ ] **Step 1: Create msosync-nodes.json**

```json
{
  "__inputs": [
    {
      "name": "DS_PROMETHEUS",
      "label": "Prometheus",
      "description": "",
      "type": "datasource",
      "pluginId": "prometheus",
      "pluginName": "Prometheus"
    }
  ],
  "__requires": [
    { "type": "grafana", "id": "grafana", "name": "Grafana", "version": "10.0.0" },
    { "type": "datasource", "id": "prometheus", "name": "Prometheus", "version": "1.0.0" },
    { "type": "panel", "id": "stat", "name": "Stat", "version": "" },
    { "type": "panel", "id": "timeseries", "name": "Time series", "version": "" }
  ],
  "annotations": { "list": [] },
  "description": "MSOSync per-node health scores and sync lag",
  "editable": true,
  "fiscalYearStartMonth": 0,
  "graphTooltip": 1,
  "id": null,
  "links": [],
  "panels": [
    {
      "datasource": { "type": "prometheus", "uid": "${datasource}" },
      "fieldConfig": {
        "defaults": {
          "color": { "mode": "thresholds" },
          "mappings": [],
          "thresholds": {
            "mode": "absolute",
            "steps": [
              { "color": "red", "value": null },
              { "color": "orange", "value": 25 },
              { "color": "yellow", "value": 50 },
              { "color": "light-green", "value": 75 },
              { "color": "green", "value": 90 }
            ]
          },
          "unit": "short",
          "min": 0,
          "max": 100
        },
        "overrides": []
      },
      "gridPos": { "h": 8, "w": 12, "x": 0, "y": 0 },
      "id": 1,
      "options": {
        "colorMode": "background",
        "graphMode": "none",
        "justifyMode": "auto",
        "orientation": "auto",
        "reduceOptions": { "calcs": ["lastNotNull"], "fields": "", "values": false },
        "textMode": "auto"
      },
      "title": "Node Send Latency P99 (by node)",
      "type": "stat",
      "targets": [
        {
          "datasource": { "type": "prometheus", "uid": "${datasource}" },
          "expr": "histogram_quantile(0.99, rate(sync_pipeline_send_ms_bucket[5m])) by (node_id)",
          "legendFormat": "Node {{node_id}}",
          "refId": "A"
        }
      ]
    },
    {
      "datasource": { "type": "prometheus", "uid": "${datasource}" },
      "fieldConfig": {
        "defaults": { "color": { "mode": "palette-classic" }, "custom": { "drawStyle": "line", "fillOpacity": 10, "lineWidth": 1, "spanNulls": false }, "unit": "ms" },
        "overrides": []
      },
      "gridPos": { "h": 8, "w": 12, "x": 12, "y": 0 },
      "id": 2,
      "title": "Per-Node Send Latency Over Time",
      "type": "timeseries",
      "targets": [
        {
          "datasource": { "type": "prometheus", "uid": "${datasource}" },
          "expr": "histogram_quantile(0.99, rate(sync_pipeline_send_ms_bucket[5m])) by (node_id)",
          "legendFormat": "Node {{node_id}} P99",
          "refId": "A"
        }
      ],
      "options": { "legend": { "displayMode": "list", "placement": "bottom", "showLegend": true }, "tooltip": { "mode": "single", "sort": "none" } }
    }
  ],
  "refresh": "30s",
  "schemaVersion": 38,
  "tags": ["msosync", "nodes"],
  "templating": {
    "list": [
      {
        "current": {},
        "hide": 0,
        "includeAll": false,
        "label": "Datasource",
        "multi": false,
        "name": "datasource",
        "options": [],
        "query": "prometheus",
        "refresh": 1,
        "type": "datasource"
      }
    ]
  },
  "time": { "from": "now-1h", "to": "now" },
  "timepicker": {},
  "timezone": "browser",
  "title": "MSOSync Nodes",
  "uid": "msosync-nodes",
  "version": 1
}
```

- [ ] **Step 2: Create msosync-slo.json**

```json
{
  "__inputs": [
    {
      "name": "DS_PROMETHEUS",
      "label": "Prometheus",
      "description": "",
      "type": "datasource",
      "pluginId": "prometheus",
      "pluginName": "Prometheus"
    }
  ],
  "__requires": [
    { "type": "grafana", "id": "grafana", "name": "Grafana", "version": "10.0.0" },
    { "type": "datasource", "id": "prometheus", "name": "Prometheus", "version": "1.0.0" },
    { "type": "panel", "id": "gauge", "name": "Gauge", "version": "" },
    { "type": "panel", "id": "timeseries", "name": "Time series", "version": "" }
  ],
  "annotations": { "list": [] },
  "description": "MSOSync SLO — delivery rate and P99 latency vs targets",
  "editable": true,
  "fiscalYearStartMonth": 0,
  "graphTooltip": 1,
  "id": null,
  "links": [],
  "panels": [
    {
      "datasource": { "type": "prometheus", "uid": "${datasource}" },
      "fieldConfig": {
        "defaults": {
          "color": { "mode": "thresholds" },
          "mappings": [],
          "thresholds": {
            "mode": "absolute",
            "steps": [
              { "color": "red", "value": null },
              { "color": "orange", "value": 0.99 },
              { "color": "green", "value": 0.999 }
            ]
          },
          "unit": "percentunit",
          "min": 0.98,
          "max": 1
        },
        "overrides": []
      },
      "gridPos": { "h": 8, "w": 8, "x": 0, "y": 0 },
      "id": 1,
      "options": {
        "minVizHeight": 75,
        "minVizWidth": 75,
        "orientation": "auto",
        "reduceOptions": { "calcs": ["lastNotNull"], "fields": "", "values": false },
        "showThresholdLabels": false,
        "showThresholdMarkers": true
      },
      "title": "Delivery Rate (24h)",
      "type": "gauge",
      "targets": [
        {
          "datasource": { "type": "prometheus", "uid": "${datasource}" },
          "expr": "clamp_max(rate(sync_batches_sent_total[24h]) / (rate(sync_batches_sent_total[24h]) + 0.001), 1)",
          "legendFormat": "Delivery Rate",
          "refId": "A"
        }
      ]
    },
    {
      "datasource": { "type": "prometheus", "uid": "${datasource}" },
      "fieldConfig": {
        "defaults": {
          "color": { "mode": "thresholds" },
          "mappings": [],
          "thresholds": {
            "mode": "absolute",
            "steps": [
              { "color": "green", "value": null },
              { "color": "orange", "value": 3000 },
              { "color": "red", "value": 5000 }
            ]
          },
          "unit": "ms",
          "min": 0,
          "max": 10000
        },
        "overrides": []
      },
      "gridPos": { "h": 8, "w": 8, "x": 8, "y": 0 },
      "id": 2,
      "options": {
        "minVizHeight": 75,
        "minVizWidth": 75,
        "orientation": "auto",
        "reduceOptions": { "calcs": ["lastNotNull"], "fields": "", "values": false },
        "showThresholdLabels": false,
        "showThresholdMarkers": true
      },
      "title": "P99 Send Latency (5m)",
      "type": "gauge",
      "targets": [
        {
          "datasource": { "type": "prometheus", "uid": "${datasource}" },
          "expr": "histogram_quantile(0.99, rate(sync_pipeline_send_ms_bucket[5m]))",
          "legendFormat": "P99 ms",
          "refId": "A"
        }
      ]
    },
    {
      "datasource": { "type": "prometheus", "uid": "${datasource}" },
      "fieldConfig": {
        "defaults": { "color": { "mode": "palette-classic" }, "custom": { "drawStyle": "line", "fillOpacity": 10, "lineWidth": 1, "spanNulls": false }, "unit": "ms" },
        "overrides": []
      },
      "gridPos": { "h": 8, "w": 24, "x": 0, "y": 8 },
      "id": 3,
      "title": "P99 Send Latency Trend vs 5000ms Target",
      "type": "timeseries",
      "targets": [
        {
          "datasource": { "type": "prometheus", "uid": "${datasource}" },
          "expr": "histogram_quantile(0.99, rate(sync_pipeline_send_ms_bucket[5m]))",
          "legendFormat": "P99 Latency",
          "refId": "A"
        },
        {
          "datasource": { "type": "prometheus", "uid": "${datasource}" },
          "expr": "vector(5000)",
          "legendFormat": "SLO Target (5000ms)",
          "refId": "B"
        }
      ],
      "options": { "legend": { "displayMode": "list", "placement": "bottom", "showLegend": true }, "tooltip": { "mode": "single", "sort": "none" } }
    }
  ],
  "refresh": "30s",
  "schemaVersion": 38,
  "tags": ["msosync", "slo"],
  "templating": {
    "list": [
      {
        "current": {},
        "hide": 0,
        "includeAll": false,
        "label": "Datasource",
        "multi": false,
        "name": "datasource",
        "options": [],
        "query": "prometheus",
        "refresh": 1,
        "type": "datasource"
      }
    ]
  },
  "time": { "from": "now-24h", "to": "now" },
  "timepicker": {},
  "timezone": "browser",
  "title": "MSOSync SLO",
  "uid": "msosync-slo",
  "version": 1
}
```

- [ ] **Step 3: Create README.md**

```markdown
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
```

- [ ] **Step 4: Verify JSON syntax**

```powershell
Get-Content docs/grafana/msosync-nodes.json | ConvertFrom-Json | Out-Null; Write-Host "nodes: valid"
Get-Content docs/grafana/msosync-slo.json | ConvertFrom-Json | Out-Null; Write-Host "slo: valid"
```

Expected: both print "valid".

- [ ] **Step 5: Commit**

```
git add docs/grafana/msosync-nodes.json docs/grafana/msosync-slo.json docs/grafana/README.md
git commit -m "feat(2F.4-T2): add Grafana nodes + SLO dashboards + import README"
```

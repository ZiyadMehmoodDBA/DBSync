# Phase 2F — Observability Design

**Status:** Approved — 2026-07-28
**Parallel track:** Runs alongside Phase 2E. 2F.1 dispatched simultaneously with 2E.1.
**Base commit:** e4cc210

---

## Overview

Phase 2F converts MSOSync's observability from an in-memory ring buffer into a production-grade telemetry platform. It wires the existing `MSOSync.Metrics` project (which already references OTel packages), adds distributed tracing through the sync pipeline, implements a per-node health scoring model with SLO tracking, publishes Grafana dashboard templates, and adds a frontend observability page.

**What already exists:**
- `MSOSync.Metrics` project with packages: `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Exporter.Prometheus.AspNetCore`
- `IMetricsService` / `InMemoryMetricsService` (1000-entry ring buffer, stays for tests)
- `ISystemHealthContributor` / `ISystemHealthService` patterns
- Serilog with Console + File sinks
- Health check endpoints (`/health/live`, `/health/ready`)
- `MetricsController` at `/api/v1/metrics`

**What 2F adds:**
- `OtelMetricsService` replacing `InMemoryMetricsService` in production DI
- Prometheus scrape endpoint at `/metrics`
- Distributed tracing through `SyncEngine` → `NodeHttpClient` → `AcknowledgementService`
- Per-node health scores (0–100) and SLO tracking
- 4 Grafana JSON dashboard templates
- `ObservabilityPage` frontend with health scores and SLO gauges

**Out of scope for 2F:** Profiling integration (perf counters), capacity forecasting, trend analysis ML, Serilog OpenTelemetry sink (can be added later as a Serilog enhancement).

---

## Architecture

`IMetricsService` remains the domain abstraction — no call-site changes. `OtelMetricsService` is the production implementation using `System.Diagnostics.Metrics.Meter`. `InMemoryMetricsService` is retained and registered in test projects only.

Distributed tracing uses `System.Diagnostics.ActivitySource` (the .NET OTel SDK's native API). No third-party tracing library. Activity spans are created in `SyncEngine`, `NodeHttpClient`, and `AcknowledgementService`, all under the source name `"MSOSync.Pipeline"`.

Health scoring is a new service layer that computes a composite 0–100 score per node from existing data (heartbeat recency, connectivity state, batch error rate, sync lag). It does not require new DB columns.

SLO tracking computes delivery rate and latency percentiles from existing `SyncBatch` and `SyncEvent` tables. No new schema.

Grafana dashboards are static JSON files operators import once. They assume a Prometheus data source named `MSOSync` and an optional Tempo/Jaeger data source named `MSOSync-Traces`.

---

## Global Constraints

- C# 13 / .NET 9, no `dynamic`
- `IMetricsService` interface unchanged — `OtelMetricsService` is a drop-in replacement
- `ActivitySource` name: `"MSOSync.Pipeline"`, version `"1.0"`
- `Meter` name: `"MSOSync"`, version `"1.0"`
- All histogram values in milliseconds (`_ms` suffix on metric names)
- Metric names: `snake_case` prefixed with `sync.` (e.g., `sync.pipeline.fetch_ms`)
- Span names: `snake_case` prefixed with `sync.` (e.g., `sync.cycle`, `sync.send`)
- Prometheus endpoint: `/metrics` (not `/api/v1/metrics`) — separate from the existing JSON endpoint
- Existing `MetricsController` at `/api/v1/metrics` retained as-is (serves in-memory snapshot for UI)
- Telemetry optional: if `Telemetry:Enabled = false` (default for Community Edition), OTel pipeline not registered; `InMemoryMetricsService` used instead
- No new DB migrations in 2F — all health scoring reads from existing tables
- React 19 / TypeScript / TanStack Query v5
- Grafana dashboards target Grafana 10+; use `"schemaVersion": 38`
- Parallel execution: 2F.1 runs first; 2F.2–2F.5 run sequentially on 2F track

---

## Sub-Phases

### 2F.1 — OpenTelemetry Foundation + Prometheus

**`OtelMetricsService`** in `MSOSync.Metrics`:
```csharp
public sealed class OtelMetricsService : IMetricsService
{
    private readonly Meter _meter = new("MSOSync", "1.0");
    private readonly ConcurrentDictionary<string, Counter<long>> _counters = new();
    private readonly ConcurrentDictionary<string, Histogram<double>> _histograms = new();

    public void IncrementCounter(string name, Dictionary<string, string>? tags = null)
    {
        var counter = _counters.GetOrAdd(name, n => _meter.CreateCounter<long>(n));
        counter.Add(1, TagsToTagList(tags));
    }

    public void RecordHistogram(string name, double valueMs, Dictionary<string, string>? tags = null)
    {
        var histogram = _histograms.GetOrAdd(name, n => _meter.CreateHistogram<double>(n, "ms"));
        histogram.Record(valueMs, TagsToTagList(tags));
    }
}
```

**Configuration:**
```json
{
  "Telemetry": {
    "Enabled": false,
    "ServiceName": "MSOSync",
    "ServiceVersion": "1.0.0",
    "OtlpEndpoint": ""
  }
}
```
When `Telemetry:Enabled = false`: `InMemoryMetricsService` registered, OTel pipeline not added.
When `Telemetry:Enabled = true`: `OtelMetricsService` registered.

**OTel pipeline registration** (in `MSOSync.Metrics.MetricsServiceExtensions`):
```csharp
services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(options.ServiceName, serviceVersion: options.ServiceVersion))
    .WithMetrics(b => b
        .AddMeter("MSOSync")
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter())
    .WithTracing(b => b
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddSource("MSOSync.Pipeline")
        .AddOtlpExporter(o => o.Endpoint = new Uri(options.OtlpEndpoint))  // only if OtlpEndpoint set
    );
```

**Prometheus endpoint** in `MSOSync.App/Program.cs`:
```csharp
app.MapPrometheusScrapingEndpoint("/metrics");
```
Protected by `RequireHost` or IP allowlist (configurable). Not behind JWT auth (Prometheus scrapes directly).

**Packages to add** (already in `MSOSync.Metrics.csproj`; verify wired):
- `OpenTelemetry.Extensions.Hosting` ✓
- `OpenTelemetry.Instrumentation.AspNetCore` ✓
- `OpenTelemetry.Exporter.Prometheus.AspNetCore` ✓
- `OpenTelemetry.Instrumentation.Runtime` (new)
- `OpenTelemetry.Instrumentation.EntityFrameworkCore` (new)

**`InMemoryMetricsService` retention:** stays in `MSOSync.Common` for test injection. Test projects continue registering it directly.

**Tests:**
- Unit: `OtelMetricsService.IncrementCounter` creates and increments correct counter. `RecordHistogram` records to histogram.
- Integration: `/metrics` endpoint returns 200 with `Content-Type: text/plain; version=0.0.4`.

---

### 2F.2 — Distributed Tracing (Sync Pipeline)

**`MSOSyncActivitySource`** in `MSOSync.Engine`:
```csharp
internal static class MSOSyncActivitySource
{
    internal static readonly ActivitySource Pipeline = new("MSOSync.Pipeline", "1.0");
}
```

**Spans added:**

In `SyncEngine.RunAsync`:
```csharp
using var cycleActivity = MSOSyncActivitySource.Pipeline.StartActivity("sync.cycle");
cycleActivity?.SetTag("node.count", nodeIds.Count);
cycleActivity?.SetTag("event.count", events.Count);
```

In `SyncEngine.DispatchNodeBatchesAsync`:
```csharp
using var dispatchActivity = MSOSyncActivitySource.Pipeline.StartActivity("sync.dispatch");
dispatchActivity?.SetTag("node.id", nodeId);
dispatchActivity?.SetTag("batch.count", batches.Count);
```

In `NodeHttpClient.SendAsync`:
```csharp
using var sendActivity = MSOSyncActivitySource.Pipeline.StartActivity("sync.send");
sendActivity?.SetTag("node.id", nodeId);
sendActivity?.SetTag("batch.id", batchId);
sendActivity?.SetTag("compressed", compressed);
sendActivity?.SetTag("content.length", contentLength);
sendActivity?.SetStatus(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
```

In `AcknowledgementService.AcknowledgeOutgoingAsync`:
```csharp
using var ackActivity = MSOSyncActivitySource.Pipeline.StartActivity("sync.ack");
ackActivity?.SetTag("batch.id", batchId.ToString());
ackActivity?.SetTag("ack.ms", stopwatch.ElapsedMilliseconds);
```

**OTLP trace export** (when `Telemetry:OtlpEndpoint` is set): already registered in 2F.1's `WithTracing` block.

**W3C TraceContext propagation:** `ActivitySource.StartActivity` propagates automatically via `HttpClient` default handler. No manual header injection needed for outbound HTTP — `NodeHttpClient` uses `HttpClient` which already propagates `traceparent`/`tracestate`.

**`OpenTelemetry.Exporter.OpenTelemetryProtocol` package** required for OTLP export. Add to `MSOSync.Metrics.csproj`.

**Tests:**
- Unit: instrument `SyncEngine.RunAsync` with a test `ActivityListener`; verify `sync.cycle` activity created with correct tags.
- Unit: verify `sync.send` activity status set to Error when `NodeHttpClient.SendAsync` throws.

---

### 2F.3 — Health Scoring Model

**`NodeHealthScore`:**
```csharp
public sealed record NodeHealthScore(
    string NodeId,
    int Score,                    // 0–100
    HealthGrade Grade,            // A (90-100), B (75-89), C (60-74), D (40-59), F (0-39)
    HealthScoreComponents Components,
    DateTimeOffset ComputedAt);

public sealed record HealthScoreComponents(
    int ConnectivityScore,        // 0–40
    int SyncLagScore,             // 0–30
    int ErrorRateScore,           // 0–20
    int HeartbeatScore);          // 0–10
```

**Scoring algorithm** in `HealthScoringService`:
- **Connectivity (40 pts):** `LifecycleState == Active && ConnectivityStatus == Reachable` → 40; `Reachable` but not Active → 20; `Unreachable` → 0; `MaintenanceMode` → 30 (not a failure)
- **Sync lag (30 pts):** last successful batch `CompletedAt` age: < 5 min → 30; 5–15 min → 20; 15–30 min → 10; > 30 min or no batches → 0
- **Error rate (20 pts):** last 100 batches: < 1% failed → 20; 1–5% → 15; 5–15% → 8; > 15% → 0
- **Heartbeat recency (10 pts):** last heartbeat: < 2 min → 10; 2–5 min → 5; > 5 min → 0

**`IHealthScoringService`:**
```csharp
public interface IHealthScoringService
{
    Task<NodeHealthScore> GetScoreAsync(string nodeId, CancellationToken ct = default);
    Task<IReadOnlyList<NodeHealthScore>> GetAllScoresAsync(CancellationToken ct = default);
}
```
Registered as singleton; caches results for 60 seconds via `IMemoryCache`.

**`ISloService`:**
```csharp
public interface ISloService
{
    Task<SloSummary> GetSummaryAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SloViolation>> GetViolationsAsync(DateTimeOffset from, CancellationToken ct = default);
}

public sealed record SloSummary(
    double DeliveryRate,          // 0.0–1.0, e.g. 0.9987
    double DeliveryRateTarget,    // from config, e.g. 0.999
    bool DeliveryRateMet,
    double LatencyP99Ms,
    double LatencyP99TargetMs,
    bool LatencyP99Met,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd);

public sealed record SloViolation(
    string NodeId,
    string Type,                  // "DeliveryRate" | "LatencyP99"
    double ActualValue,
    double TargetValue,
    DateTimeOffset OccurredAt);
```

**SLO configuration:**
```json
{
  "Slo": {
    "WindowHours": 24,
    "DeliveryRateTarget": 0.999,
    "LatencyP99TargetMs": 5000
  }
}
```

**`HealthScoreController`:**
- `GET /api/v1/health/scores` — all node scores (requires OperatorOrAbove)
- `GET /api/v1/health/scores/{nodeId}` — single node score

**`SloController`:**
- `GET /api/v1/slo/summary` — current SLO window summary
- `GET /api/v1/slo/violations?from=` — violations in window

**Tests:**
- Unit: scoring algorithm with known data (mock `SyncNode`, `SyncBatch` data). Test each score band boundary.
- Unit: `SloService` computes correct delivery rate from mock batch success/failure counts.

---

### 2F.4 — Grafana Dashboard Templates

**Directory:** `docs/grafana/`

**4 dashboard JSON files:**

**`msosync-overview.json`** — Platform Overview
- Panels: sync batches/min (time series), active nodes count (stat), batch success rate % (gauge), error rate (time series), top 5 erroring nodes (table)
- Variables: `$datasource` (Prometheus), `$interval`

**`msosync-pipeline.json`** — Pipeline Latency
- Panels: `sync.pipeline.fetch_ms` histogram (heatmap), `sync.pipeline.compress_ms` p50/p95/p99 (time series), `sync.pipeline.send_ms` p50/p95/p99, `sync.pipeline.ack_ms` p50/p95/p99, batch size distribution (histogram)
- All latency panels use PromQL `histogram_quantile`

**`msosync-nodes.json`** — Node Health
- Panels: node health score heatmap (all nodes × time), per-node connectivity status table, sync lag by node (bar gauge), last heartbeat age (table)
- Variable: `$node` (multi-select from `sync_node_id` label)

**`msosync-slo.json`** — SLO Tracking
- Panels: delivery rate gauge (target line at 99.9%), latency p99 gauge (target line at 5000ms), SLO burn rate (time series), 30-day SLO compliance calendar heatmap
- Uses recording rules (documented in `docs/grafana/README.md`)

**`docs/grafana/README.md`:**
- Import instructions (Grafana UI → Dashboards → Import → Upload JSON)
- Required data source names: Prometheus `MSOSync`, tracing `MSOSync-Traces` (optional)
- Required Prometheus scrape config snippet
- Recommended recording rules for SLO burn rate (PromQL)
- Grafana version requirement: 10.0+

**Tests:** JSON schema validation — each dashboard JSON parses without error, contains required `title`, `uid`, `panels` fields. Automated via a simple xUnit test that deserializes and checks structure.

---

### 2F.5 — Frontend Observability UI

**`ObservabilityPage.tsx`** in `src/features/observability/`
- Route: `/administration/observability` (AdminOnly permission, `PermissionKeys.Admin`)
- Tabs: **Overview**, **Pipeline**, **Nodes**, **SLO**

**Overview tab:**
- `useHealthScores()` — fetches `/api/v1/health/scores`, polls every 60s
- `useSloBurnRate()` — fetches `/api/v1/slo/summary`, refreshes every 5 min
- `PlatformHealthCard` — overall health score (average of all node scores), grade badge
- `SloSummaryCard` — delivery rate + latency p99 vs targets, met/not-met indicator

**Pipeline tab:**
- `usePipelineMetrics()` — fetches `/api/v1/metrics` (existing endpoint)
- `PipelineLatencyChart` — recharts `BarChart` showing fetch/compress/send/ack latency (p50/p95/p99 from in-memory metrics if OTel not enabled, or link to Grafana if configured)
- Grafana link: when `Observability:GrafanaUrl` configured in appsettings, show "Open in Grafana →" link

**Nodes tab:**
- `NodeHealthTable` — all nodes with health score, grade badge (colour-coded A=green, B=lime, C=yellow, D=orange, F=red), connectivity status, last heartbeat, sync lag
- Sortable by score, node ID, last heartbeat
- Click → navigate to existing node detail page

**SLO tab:**
- `SloGauge` — delivery rate: needle gauge with target marker at configured target %
- `SloGauge` — latency p99: same pattern
- `SloViolationsTable` — recent violations from `/api/v1/slo/violations?from=7d`

**`useHealthScores` hook:**
```typescript
export function useHealthScores() {
  return useQuery({
    queryKey: queryKeys.health.scores(),
    queryFn: () => healthApi.getScores(),
    refetchInterval: 60_000,
  });
}
```

**`useSloBurnRate` hook:**
```typescript
export function useSloBurnRate() {
  return useQuery({
    queryKey: queryKeys.slo.summary(),
    queryFn: () => sloApi.getSummary(),
    refetchInterval: 300_000,
  });
}
```

**New query key groups in `queryKeys.ts`:**
```typescript
health: {
  scores: () => ['health', 'scores'] as const,
  score: (nodeId: string) => ['health', 'scores', nodeId] as const,
},
slo: {
  summary: () => ['slo', 'summary'] as const,
  violations: (from: string) => ['slo', 'violations', from] as const,
},
```

**Nav:** Add `Activity` (lucide) icon to admin nav section, route `/administration/observability`, `requiredPermission: AdminOnly`.

**Config for Grafana link:**
```json
{
  "Observability": {
    "GrafanaUrl": ""
  }
}
```
Exposed via a new `GET /api/v1/config/observability` endpoint (no auth required — only non-sensitive config values).

**Tests:**
- `ObservabilityPage.test.tsx` — 6 tests: renders health scores table, renders SLO gauges, shows grade badge colour, shows Grafana link when configured, handles empty node list, handles API error with ErrorState.

---

## Execution Order

```
2F.1 (OTel Foundation + Prometheus)    ← starts simultaneously with 2E.1
    ↓
2F.2 (Distributed Tracing)
    ↓
2F.3 (Health Scoring + SLO)
    ↓
2F.4 (Grafana Dashboards)
    ↓
2F.5 (Frontend Observability UI)
```

Sequential within 2F track; parallel with 2E track. No DB migrations in 2F.

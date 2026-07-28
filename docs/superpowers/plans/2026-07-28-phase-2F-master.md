# Phase 2F — Observability Master Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert MSOSync from in-memory ring buffer metrics to a production-grade OpenTelemetry observability platform with Prometheus export, sync pipeline distributed tracing, per-node health scoring, SLO tracking, Grafana dashboards, and a frontend observability page.

**Architecture:** Five sequential sub-phases on a dedicated track running in parallel with Phase 2E. 2F.1 (OTel foundation) must land first; IMetricsService abstraction is preserved — OtelMetricsService is a drop-in replacement. No DB migrations; all health/SLO data reads from existing tables.

**Tech Stack:** C# 13 / .NET 9 / OpenTelemetry .NET SDK / System.Diagnostics.Metrics / System.Diagnostics.ActivitySource / OpenTelemetry.Exporter.Prometheus.AspNetCore / OpenTelemetry.Exporter.OpenTelemetryProtocol / React 19 / TypeScript / TanStack Query v5 / recharts

## Global Constraints

- C# 13 / .NET 9, no `dynamic`
- `IMetricsService` interface unchanged — `OtelMetricsService` is a drop-in replacement; all existing call sites unchanged
- `ActivitySource` name: `"MSOSync.Pipeline"`, version `"1.0"`
- `Meter` name: `"MSOSync"`, version `"1.0"`
- All histogram values in milliseconds (metric names carry `_ms` suffix)
- Metric names: `sync.pipeline.fetch_ms`, `sync.pipeline.compress_ms`, `sync.pipeline.send_ms`, `sync.pipeline.ack_ms`
- Span names: `sync.cycle`, `sync.dispatch`, `sync.send`, `sync.ack`
- Prometheus endpoint: `/metrics` (not `/api/v1/metrics`)
- `Telemetry:Enabled` defaults `false`; when false, `InMemoryMetricsService` used (existing behaviour)
- `InMemoryMetricsService` stays in `MSOSync.Common`; test projects inject it directly
- No DB migrations in Phase 2F
- React 19 / TanStack Query v5 — no `onSuccess`/`onError` on `useQuery`
- Grafana dashboard JSON: `schemaVersion: 38`, Grafana 10+
- `git add` by file name only — never `git add -A` or `git add .`

---

## Sub-Phases

| Sub-phase | Plan file | Tasks | Migration |
|---|---|---|---|
| 2F.1 OTel Foundation + Prometheus | [2026-07-28-phase-2F-1-otel-foundation.md](2026-07-28-phase-2F-1-otel-foundation.md) | 3 | none |
| 2F.2 Distributed Tracing | [2026-07-28-phase-2F-2-distributed-tracing.md](2026-07-28-phase-2F-2-distributed-tracing.md) | 3 | none |
| 2F.3 Health Scoring + SLO | [2026-07-28-phase-2F-3-health-scoring-slo.md](2026-07-28-phase-2F-3-health-scoring-slo.md) | 4 | none |
| 2F.4 Grafana Dashboards | [2026-07-28-phase-2F-4-grafana-dashboards.md](2026-07-28-phase-2F-4-grafana-dashboards.md) | 2 | none |
| 2F.5 Frontend Observability UI | [2026-07-28-phase-2F-5-frontend-observability.md](2026-07-28-phase-2F-5-frontend-observability.md) | 4 | none |

## Execution Order

```
2F.1 → 2F.2 → 2F.3 → 2F.4 → 2F.5
(parallel with Phase 2E; 2F.1 starts simultaneously with 2E.1)
```

## Spec Reference

`docs/superpowers/specs/2026-07-28-phase-2F-observability.md`

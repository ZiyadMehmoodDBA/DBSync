# Phase 2D.4 — Baseline Benchmark Results

**Run date:** TBD — fill in after first manual run
**Machine:** [fill in CPU + RAM]
**LocalDB version:** [fill in]
**Dataset:** 1000 nodes / 200 groups / 400 routers / 1 trigger / 400 trigger-router mappings

## Results

| Benchmark | Method | Mean | StdDev | Target |
|---|---|---|---|---|
| TopologyGraphBenchmark | GetTopologyGraph_1000Nodes | [fill] ms | [fill] ms | < 500 ms |
| TopologyGraphBenchmark | GetTopologyGraph_WithNodeIdFilter | [fill] ms | [fill] ms | — |
| NodeCursorPageBenchmark | FirstPage | [fill] ms | [fill] ms | < 50 ms |
| NodeCursorPageBenchmark | Page5 | [fill] ms | [fill] ms | < 50 ms |
| NodeCursorPageBenchmark | Page20 | [fill] ms | [fill] ms | < 50 ms |
| BulkFanOutBenchmark | FanOut_1000Nodes_SingleBulkInsert | [fill] ms | [fill] ms | < 100 ms |
| DashboardSummaryBenchmark | GetSummary_CacheMiss | [fill] ms | [fill] ms | < 100 ms |
| DashboardSummaryBenchmark | GetSummary_CacheHit | [fill] µs | [fill] µs | < 1 ms |

## How to run

```bash
dotnet run -c Release --project src/MSOSync.Benchmarks/MSOSync.Benchmarks.csproj
```

To run a single benchmark:

```bash
dotnet run -c Release --project src/MSOSync.Benchmarks/MSOSync.Benchmarks.csproj -- --filter *TopologyGraph*
```

Results are saved to `BenchmarkDotNet.Artifacts/results/` after the run.

## Notes

[Add any deviations from targets, recommendations for further optimisation, or observations about index usage.]

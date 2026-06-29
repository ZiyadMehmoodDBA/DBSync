# Task 1: Metrics DTOs

**Part of:** [Epic 9C Plan](2026-06-29-epic9c-metrics-apis.md)

**Goal:** Create 5 DTO files in `src/MSOSync.Metadata/Metrics/`. No tests needed — pure data-shape definitions. End with a clean build.

**Files:**
- Create: `src/MSOSync.Metadata/Metrics/MetricsSummaryDto.cs`
- Create: `src/MSOSync.Metadata/Metrics/NodeMetricsDto.cs`
- Create: `src/MSOSync.Metadata/Metrics/ChannelMetricsDto.cs`
- Create: `src/MSOSync.Metadata/Metrics/RuntimeMetricsDto.cs`
- Create: `src/MSOSync.Metadata/Metrics/MonitorMetricDto.cs`

**Interfaces:**
- Consumes: `MSOSync.Persistence.ConnectivityStatus` (already exists)
- Produces (used in Task 2 and 3):
  - `MetricsSummaryDto(int TotalNodes, int ReachableNodes, int DegradedNodes, int UnreachableNodes, int UnknownNodes, long IncomingQueueDepth, long OutgoingQueueDepth, long BatchesProcessed24h, long Errors24h, double ErrorRatePercent, double ThroughputPerMinute, DateTime GeneratedAt)`
  - `NodeMetricsDto(string NodeId, string GroupId, ConnectivityStatus ConnectivityStatus, long IncomingQueueDepth, long OutgoingQueueDepth, int ProcessedBatches24h, int Errors24h, double? AvgApplyTimeMs, DateTime? LastHeartbeat)`
  - `ChannelMetricsDto(string ChannelId, int ActiveNodes, long PendingEvents, long PendingOutgoingBatches, long ProcessedBatches24h, int Errors24h, double ThroughputPerMinute)`
  - `RuntimeMetricsDto(long? HeapUsed, long? HeapMax, int? ThreadCount, decimal? CpuPercent, long? GcCount, long? GcTimeMs, long? UptimeMs, DateTime CreateTime)` — **no NodeId** (`SyncRuntimeStats` has no NodeId column)
  - `MonitorMetricDto(string? NodeId, string? MetricName, string? MetricValue, DateTime CreateTime)`

---

- [ ] **Step 1: Create MetricsSummaryDto.cs**

Create `src/MSOSync.Metadata/Metrics/MetricsSummaryDto.cs`:

```csharp
namespace MSOSync.Metadata.Metrics;

public sealed record MetricsSummaryDto(
    int      TotalNodes,
    int      ReachableNodes,
    int      DegradedNodes,
    int      UnreachableNodes,
    int      UnknownNodes,
    long     IncomingQueueDepth,
    long     OutgoingQueueDepth,
    long     BatchesProcessed24h,
    long     Errors24h,
    double   ErrorRatePercent,
    double   ThroughputPerMinute,
    DateTime GeneratedAt);
```

- [ ] **Step 2: Create NodeMetricsDto.cs**

Create `src/MSOSync.Metadata/Metrics/NodeMetricsDto.cs`:

```csharp
using MSOSync.Persistence;

namespace MSOSync.Metadata.Metrics;

public sealed record NodeMetricsDto(
    string             NodeId,
    string             GroupId,
    ConnectivityStatus ConnectivityStatus,
    long               IncomingQueueDepth,
    long               OutgoingQueueDepth,
    int                ProcessedBatches24h,
    int                Errors24h,
    double?            AvgApplyTimeMs,
    DateTime?          LastHeartbeat);
```

- [ ] **Step 3: Create ChannelMetricsDto.cs**

Create `src/MSOSync.Metadata/Metrics/ChannelMetricsDto.cs`:

```csharp
namespace MSOSync.Metadata.Metrics;

public sealed record ChannelMetricsDto(
    string  ChannelId,
    int     ActiveNodes,
    long    PendingEvents,
    long    PendingOutgoingBatches,
    long    ProcessedBatches24h,
    int     Errors24h,
    double  ThroughputPerMinute);
```

`ThroughputPerMinute = Math.Round(ProcessedBatches24h / 1440.0, 2)` — computed in C#, not stored.

- [ ] **Step 4: Create RuntimeMetricsDto.cs**

Create `src/MSOSync.Metadata/Metrics/RuntimeMetricsDto.cs`:

```csharp
namespace MSOSync.Metadata.Metrics;

public sealed record RuntimeMetricsDto(
    long?    HeapUsed,
    long?    HeapMax,
    int?     ThreadCount,
    decimal? CpuPercent,
    long?    GcCount,
    long?    GcTimeMs,
    long?    UptimeMs,
    DateTime CreateTime);
```

Note: `SyncRuntimeStats` has no `NodeId` field — the DTO intentionally omits it.

- [ ] **Step 5: Create MonitorMetricDto.cs**

Create `src/MSOSync.Metadata/Metrics/MonitorMetricDto.cs`:

```csharp
namespace MSOSync.Metadata.Metrics;

public sealed record MonitorMetricDto(
    string?  NodeId,
    string?  MetricName,
    string?  MetricValue,
    DateTime CreateTime);
```

Note: `SyncMonitor.CreateTime` is `DateTime?` in the entity but the schema guarantees it is set — project uses `r.CreateTime!.Value` in the service.

- [ ] **Step 6: Build and verify**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 7: Commit**

```powershell
git add src/MSOSync.Metadata/Metrics/MetricsSummaryDto.cs
git add src/MSOSync.Metadata/Metrics/NodeMetricsDto.cs
git add src/MSOSync.Metadata/Metrics/ChannelMetricsDto.cs
git add src/MSOSync.Metadata/Metrics/RuntimeMetricsDto.cs
git add src/MSOSync.Metadata/Metrics/MonitorMetricDto.cs
git commit -m "feat(9c): add metrics DTOs"
```

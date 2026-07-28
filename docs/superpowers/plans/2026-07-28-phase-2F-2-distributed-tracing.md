# Phase 2F.2 — Distributed Tracing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Instrument the sync pipeline with `System.Diagnostics.ActivitySource` spans — `sync.cycle`, `sync.dispatch`, `sync.send`, `sync.ack` — exported via OTel when `Telemetry:Enabled = true`.

**Architecture:** `PipelineActivitySource` holds a single static `ActivitySource("MSOSync.Pipeline", "1.0")`. SyncJob wraps its full cycle in `sync.cycle` and each per-node dispatch in `sync.dispatch`. The HTTP send and ack processing wrap in `sync.send` and `sync.ack`. When `Telemetry:Enabled = false`, `StartActivity()` returns `null` and all `?.` calls are no-ops — zero overhead, zero call-site changes to `IMetricsService`.

**Tech Stack:** C# 13 / .NET 9 / System.Diagnostics.ActivitySource / OpenTelemetry.Exporter.OpenTelemetryProtocol (already in MSOSync.Metrics from 2F.1)

## Global Constraints

- Prerequisite: 2F.1 complete — `OtelMetricsService`, `MetricsServiceExtensions`, `TelemetryOptions` exist
- `ActivitySource` name: `"MSOSync.Pipeline"`, version: `"1.0"`
- Span names (exact): `sync.cycle`, `sync.dispatch`, `sync.send`, `sync.ack`
- Always use `?.` null-safe calls on `Activity` — `StartActivity()` returns `null` when no listener
- `IMetricsService` and all existing metric call sites: unchanged
- `git add` by file name only

---

### Task 1: PipelineActivitySource + sync.cycle + sync.dispatch spans

**Files:**
- Create: `src/MSOSync.Metrics/PipelineActivitySource.cs`
- Modify: `src/MSOSync.Metrics/MetricsServiceExtensions.cs` (verify `.AddSource("MSOSync.Pipeline")` present)
- Modify: SyncJob file (locate via `Get-ChildItem -Recurse -Filter "SyncJob.cs" src/`) — add sync.cycle + sync.dispatch

**Interfaces:**
- Consumes: `MetricsServiceExtensions.AddTelemetry` (2F.1)
- Produces: `PipelineActivitySource.Source` static `ActivitySource` — used by SyncJob, HTTP client, ack service

- [ ] **Step 1: Create PipelineActivitySource**

```csharp
// src/MSOSync.Metrics/PipelineActivitySource.cs
using System.Diagnostics;

namespace MSOSync.Metrics;

public static class PipelineActivitySource
{
    public static readonly ActivitySource Source = new("MSOSync.Pipeline", "1.0");
}
```

- [ ] **Step 2: Verify OTel registration in MetricsServiceExtensions**

Read `src/MSOSync.Metrics/MetricsServiceExtensions.cs`. Confirm the `WithTracing` block contains `.AddSource("MSOSync.Pipeline")`:

```csharp
.WithTracing(b =>
{
    b.AddAspNetCoreInstrumentation()
     .AddEntityFrameworkCoreInstrumentation()
     .AddSource("MSOSync.Pipeline");   // ← must be present

    if (!string.IsNullOrEmpty(opts.OtlpEndpoint))
        b.AddOtlpExporter(o => o.Endpoint = new Uri(opts.OtlpEndpoint));
});
```

If missing, add `.AddSource("MSOSync.Pipeline")` before the `if (!string.IsNullOrEmpty(opts.OtlpEndpoint))` line.

- [ ] **Step 3: Locate SyncJob**

```powershell
Get-ChildItem -Recurse -Filter "SyncJob.cs" src/ | Select-Object -ExpandProperty FullName
```

Read the found file. Identify:
1. The main execution method body (usually inside an `ExecuteAsync` override or similar loop)
2. The per-node dispatch call site (the loop that iterates active nodes and triggers data send)

- [ ] **Step 4: Add sync.cycle and sync.dispatch spans to SyncJob**

Add `using System.Diagnostics; using MSOSync.Metrics;` at the top of SyncJob.cs if not present.

Wrap the main cycle body:

```csharp
// Around the existing per-cycle execution body:
using var cycleActivity = PipelineActivitySource.Source.StartActivity("sync.cycle");
try
{
    cycleActivity?.SetTag("job.type", "sync");

    // --- existing logic that iterates nodes and dispatches ---
    // For each node dispatch, wrap like this:
    foreach (var node in activeNodes)  // adapt variable name to match existing code
    {
        using var dispatchActivity = PipelineActivitySource.Source.StartActivity("sync.dispatch");
        dispatchActivity?.SetTag("node.id", node.NodeId.ToString());

        // ... existing per-node work (unchanged) ...
    }
    // --- end existing logic ---

    cycleActivity?.SetTag("node.count", activeNodes.Count);
    cycleActivity?.SetStatus(ActivityStatusCode.Ok);
}
catch (Exception ex)
{
    cycleActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    throw;
}
```

Adapt variable names to match the actual SyncJob code. Preserve all existing logic; only add activity wrapping.

- [ ] **Step 5: Build**

```
dotnet build src/MSOSync.App/MSOSync.App.csproj 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

Get the exact SyncJob.cs path from Step 3 and use it:

```
git add src/MSOSync.Metrics/PipelineActivitySource.cs src/MSOSync.Metrics/MetricsServiceExtensions.cs <exact-path-to-SyncJob.cs>
git commit -m "feat(2F.2-T1): add PipelineActivitySource + sync.cycle/dispatch spans in SyncJob"
```

---

### Task 2: sync.send + sync.ack spans

**Files:**
- Modify: HTTP node-send file (locate via `Get-ChildItem -Recurse -Include "*HttpClient*","*NodeClient*","*MsoSync*Http*" src/`)
- Modify: Ack processing file (locate via `Get-ChildItem -Recurse -Include "*Ack*" src/ | Where-Object { $_.Name -notlike "*Test*" }`)

**Interfaces:**
- Consumes: `PipelineActivitySource.Source` (Task 1)
- Produces: `sync.send` span wrapping HTTP batch send; `sync.ack` span wrapping ack processing

- [ ] **Step 1: Locate HTTP send and ack files**

```powershell
Get-ChildItem -Recurse -Include "*HttpClient*","*NodeClient*","*MsoSync*Http*" src/ | Select-Object FullName
Get-ChildItem -Recurse -Include "*Ack*" src/ | Where-Object { $_.Name -notlike "*Test*" } | Select-Object FullName
```

Read each found file. From prior context, the send file is likely `MsoSyncHttpClient.cs` with method `PostMultipartAsync`. Identify the exact method that sends batch data to a sync node, and the method that processes node acknowledgments.

- [ ] **Step 2: Add sync.send span**

In the file that sends data to sync nodes (e.g., `PostMultipartAsync` or similar), wrap the HTTP call:

```csharp
// Add using directives if not present:
using System.Diagnostics;
using MSOSync.Metrics;

// Wrap the existing send logic:
using var activity = PipelineActivitySource.Source.StartActivity("sync.send");
activity?.SetTag("node.id", nodeId.ToString());   // adapt parameter name
activity?.SetTag("batch.id", batchId.ToString()); // adapt if available

try
{
    // ... existing HTTP call (unchanged) ...
    activity?.SetTag("http.status_code", (int)response.StatusCode);
    activity?.SetStatus(response.IsSuccessStatusCode ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    return response; // adapt return type to actual method
}
catch (Exception ex)
{
    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    throw;
}
```

- [ ] **Step 3: Add sync.ack span**

In the ack processing method, wrap the acknowledgment logic:

```csharp
using var activity = PipelineActivitySource.Source.StartActivity("sync.ack");
activity?.SetTag("node.id", nodeId.ToString());  // adapt parameter name

try
{
    // ... existing ack processing (unchanged) ...
    activity?.SetStatus(ActivityStatusCode.Ok);
}
catch (Exception ex)
{
    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    throw;
}
```

- [ ] **Step 4: Build**

```
dotnet build src/MSOSync.App/MSOSync.App.csproj 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

Use exact file paths found in Step 1:

```
git add <http-send-file-path> <ack-file-path>
git commit -m "feat(2F.2-T2): add sync.send + sync.ack ActivitySource spans"
```

---

### Task 3: ActivityListener tests + full test suite verification

**Files:**
- Create: `tests/MSOSync.MetricsTests/PipelineActivitySourceTests.cs`

**Interfaces:**
- Consumes: `PipelineActivitySource.Source` (Task 1)
- Produces: tests verifying span names, tags, parent-child relationships, null-safe fallback

- [ ] **Step 1: Write tests**

```csharp
// tests/MSOSync.MetricsTests/PipelineActivitySourceTests.cs
using System.Diagnostics;
using FluentAssertions;
using MSOSync.Metrics;
using Xunit;

namespace MSOSync.MetricsTests;

public sealed class PipelineActivitySourceTests : IDisposable
{
    private readonly ActivityListener _listener = new();
    private readonly List<Activity> _completed = [];

    public PipelineActivitySourceTests()
    {
        _listener.ShouldListenTo = source => source.Name == "MSOSync.Pipeline";
        _listener.Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
            ActivitySamplingResult.AllDataAndRecorded;
        _listener.ActivityStopped = activity => _completed.Add(activity);
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void Source_HasCorrectName()
    {
        PipelineActivitySource.Source.Name.Should().Be("MSOSync.Pipeline");
    }

    [Fact]
    public void StartActivity_SyncCycle_IsNotNull_WhenListenerRegistered()
    {
        using var activity = PipelineActivitySource.Source.StartActivity("sync.cycle");
        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("sync.cycle");
    }

    [Fact]
    public void StartActivity_SyncDispatch_IncludesNodeIdTag()
    {
        using var dispatch = PipelineActivitySource.Source.StartActivity("sync.dispatch");
        dispatch?.SetTag("node.id", "node-42");

        dispatch.Should().NotBeNull();
        dispatch!.Tags.Should().Contain(t => t.Key == "node.id" && t.Value == "node-42");
    }

    [Fact]
    public void StartActivity_SyncDispatch_IsChildOf_SyncCycle()
    {
        using var cycle = PipelineActivitySource.Source.StartActivity("sync.cycle");
        using var dispatch = PipelineActivitySource.Source.StartActivity("sync.dispatch");

        cycle.Should().NotBeNull();
        dispatch.Should().NotBeNull();
        dispatch!.ParentId.Should().Be(cycle!.Id);
    }

    [Fact]
    public void StartActivity_ReturnsNull_WhenSourceHasNoListener()
    {
        // An isolated source with no registered listener returns null —
        // this is the safe no-op behavior callers rely on when OTel is disabled.
        using var isolated = new ActivitySource("MSOSync.Pipeline.IsolatedTest");
        using var activity = isolated.StartActivity("sync.cycle");

        activity.Should().BeNull();
    }

    [Fact]
    public void StartActivity_SyncSend_IncludesHttpStatusTag()
    {
        using var send = PipelineActivitySource.Source.StartActivity("sync.send");
        send?.SetTag("http.status_code", 200);

        send.Should().NotBeNull();
        send!.Tags.Should().Contain(t => t.Key == "http.status_code" && t.Value == "200");
    }

    [Fact]
    public void StartActivity_CompletedActivity_IsRecordedByListener()
    {
        using (var activity = PipelineActivitySource.Source.StartActivity("sync.ack"))
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
        }

        _completed.Should().ContainSingle(a => a.OperationName == "sync.ack"
            && a.Status == ActivityStatusCode.Ok);
    }
}
```

- [ ] **Step 2: Run metrics tests**

```
dotnet test tests/MSOSync.MetricsTests -v minimal
```

Expected: `Passed: 10+, Failed: 0`

- [ ] **Step 3: Run full test suite (excluding integration tests)**

```
dotnet test --filter "FullyQualifiedName!~IntegrationTest" -v minimal 2>&1 | tail -10
```

Expected: no regressions.

- [ ] **Step 4: Commit**

```
git add tests/MSOSync.MetricsTests/PipelineActivitySourceTests.cs
git commit -m "feat(2F.2-T3): add ActivityListener tests for pipeline spans"
```

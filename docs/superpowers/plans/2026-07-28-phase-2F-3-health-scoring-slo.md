# Phase 2F.3 — Health Scoring + SLO Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Compute a 0–100 composite health score for each sync node and track SLO compliance (delivery rate + P99 latency) over a configurable rolling window — all read from existing tables, no new migrations.

**Architecture:** `IHealthScoringService` computes scores from existing node/connectivity/batch data. `ISloService` reads batch completion times and status to compute delivery rate and P99 latency. Both exposed via REST endpoints. No new DB tables — reads existing `SyncNodes`, connectivity events, and batch history.

**Tech Stack:** C# 13 / .NET 9 / EF Core 9 (read-only queries) / xUnit + FluentAssertions + Moq

## Global Constraints

- Prerequisite: 2F.1 complete — `OtelMetricsService`, `MetricsServiceExtensions` exist
- No DB migrations in 2F.3
- Health score formula: Connectivity(0–40) + SyncLag(0–30) + ErrorRate(0–20) + HeartbeatRecency(0–10)
- Grade thresholds: A ≥ 90, B ≥ 75, C ≥ 50, D ≥ 25, F < 25
- SLO defaults: `Slo:DeliveryRateTarget = 0.999`, `Slo:LatencyP99TargetMs = 5000`, `Slo:WindowHours = 24`
- All new admin endpoints: `[Authorize(Policy = "AdminOnly")]`
- `git add` by file name only

---

### Task 1: NodeHealthScore model + IHealthScoringService + HealthScoringService

**Files:**
- Create: `src/MSOSync.Api/Health/NodeHealthScore.cs`
- Create: `src/MSOSync.Api/Health/IHealthScoringService.cs`
- Create: `src/MSOSync.Api/Health/HealthScoringService.cs`
- Create: `tests/MSOSync.ApiTests/Health/HealthScoringServiceTests.cs`

**Interfaces:**
- Consumes: `MSOSyncDbContext` (read-only queries on existing SyncNode, connectivity, batch tables — read the schema to find exact table/column names)
- Produces: `NodeHealthScore { NodeId, NodeName, Score, Grade, ConnectivityScore, SyncLagScore, ErrorRateScore, HeartbeatScore, ComputedAt }`, `IHealthScoringService.GetScoresAsync(ct) : Task<IReadOnlyList<NodeHealthScore>>`

- [ ] **Step 1: Explore existing DB schema**

Read these files to understand the relevant entities:

```powershell
Get-ChildItem -Recurse -Include "SyncNode*.cs","*Node*.cs" src/MSOSync.Persistence/Entities/ | Select-Object FullName
Get-ChildItem -Recurse -Include "*Batch*.cs","*Event*.cs","*Connectivity*.cs" src/MSOSync.Persistence/Entities/ | Select-Object FullName
```

Identify:
- Table/entity for sync nodes (likely `SyncNode`) — fields for `IsReachable`/connectivity, last heartbeat, node name
- Table/entity for sync batches — fields for status (success/failure), completion time, node reference
- Any existing connectivity/event tables

Note these names — replace all entity and property references in Steps below with actual names.

- [ ] **Step 2: Create NodeHealthScore**

```csharp
// src/MSOSync.Api/Health/NodeHealthScore.cs
namespace MSOSync.Api.Health;

public sealed record NodeHealthScore(
    int NodeId,
    string NodeName,
    int Score,
    string Grade,
    int ConnectivityScore,
    int SyncLagScore,
    int ErrorRateScore,
    int HeartbeatScore,
    DateTime ComputedAt)
{
    public static string ComputeGrade(int score) => score switch
    {
        >= 90 => "A",
        >= 75 => "B",
        >= 50 => "C",
        >= 25 => "D",
        _ => "F",
    };
}
```

- [ ] **Step 3: Write failing tests**

```csharp
// tests/MSOSync.ApiTests/Health/HealthScoringServiceTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Health;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.ApiTests.Health;

public sealed class HealthScoringServiceTests : IDisposable
{
    private readonly MSOSyncDbContext _db;

    public HealthScoringServiceTests()
    {
        var options = new DbContextOptionsBuilder<MSOSyncDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new MSOSyncDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetScoresAsync_ReturnsScore_ForEachNode()
    {
        // Seed a reachable node with recent heartbeat and no errors
        // Adapt entity names and properties to actual schema found in Step 1
        _db.SyncNodes.Add(new SyncNode
        {
            Id = 1,
            Name = "Node A",
            IsReachable = true,
            LastHeartbeatAt = DateTime.UtcNow.AddMinutes(-2),
            LastSyncAt = DateTime.UtcNow.AddMinutes(-1),
        });
        await _db.SaveChangesAsync();

        var svc = new HealthScoringService(_db);
        var scores = await svc.GetScoresAsync();

        scores.Should().ContainSingle(s => s.NodeId == 1);
        scores[0].ConnectivityScore.Should().Be(40); // reachable = full score
        scores[0].HeartbeatScore.Should().Be(10);    // heartbeat < 5min = full score
    }

    [Fact]
    public async Task GetScoresAsync_Score0Connectivity_WhenNodeUnreachable()
    {
        _db.SyncNodes.Add(new SyncNode
        {
            Id = 2,
            Name = "Node B",
            IsReachable = false,
            LastHeartbeatAt = DateTime.UtcNow.AddHours(-2),
            LastSyncAt = DateTime.UtcNow.AddHours(-1),
        });
        await _db.SaveChangesAsync();

        var svc = new HealthScoringService(_db);
        var scores = await svc.GetScoresAsync();

        scores.Single(s => s.NodeId == 2).ConnectivityScore.Should().Be(0);
    }

    [Fact]
    public void ComputeGrade_ReturnsCorrectGrade()
    {
        NodeHealthScore.ComputeGrade(95).Should().Be("A");
        NodeHealthScore.ComputeGrade(80).Should().Be("B");
        NodeHealthScore.ComputeGrade(60).Should().Be("C");
        NodeHealthScore.ComputeGrade(30).Should().Be("D");
        NodeHealthScore.ComputeGrade(10).Should().Be("F");
    }
}
```

Note: adapt `SyncNode` property names (`IsReachable`, `LastHeartbeatAt`, `LastSyncAt`, `Name`) to the actual entity properties found in Step 1.

- [ ] **Step 4: Run tests — expect compile failure**

```
dotnet test tests/MSOSync.ApiTests -v minimal 2>&1 | head -10
```

- [ ] **Step 5: Create IHealthScoringService**

```csharp
// src/MSOSync.Api/Health/IHealthScoringService.cs
namespace MSOSync.Api.Health;

public interface IHealthScoringService
{
    Task<IReadOnlyList<NodeHealthScore>> GetScoresAsync(CancellationToken ct = default);
    Task<NodeHealthScore?> GetScoreAsync(int nodeId, CancellationToken ct = default);
}
```

- [ ] **Step 6: Implement HealthScoringService**

Adapt all entity/property names based on the actual schema found in Step 1.

```csharp
// src/MSOSync.Api/Health/HealthScoringService.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Api.Health;

internal sealed class HealthScoringService(MSOSyncDbContext db) : IHealthScoringService
{
    public async Task<IReadOnlyList<NodeHealthScore>> GetScoresAsync(CancellationToken ct = default)
    {
        // Adapt entity name (SyncNode) and properties to actual schema
        var nodes = await db.SyncNodes.AsNoTracking().ToListAsync(ct);
        var now = DateTime.UtcNow;

        // Compute error rate per node over last 24h
        var since = now.AddHours(-24);
        // Adapt to actual batch entity name and properties
        var batchStats = await db.SyncBatches
            .AsNoTracking()
            .Where(b => b.CreatedAt >= since)
            .GroupBy(b => b.NodeId)
            .Select(g => new
            {
                NodeId = g.Key,
                Total = g.Count(),
                Errors = g.Count(b => b.Status == "Failed"), // adapt status field/value
            })
            .ToListAsync(ct);

        var statsByNode = batchStats.ToDictionary(s => s.NodeId);

        return nodes.Select(node =>
        {
            var conn = node.IsReachable ? 40 : 0;

            var lag = node.LastSyncAt is null ? 0
                : (now - node.LastSyncAt.Value).TotalMinutes switch
                {
                    < 1 => 30,
                    < 5 => 20,
                    < 30 => 10,
                    _ => 0,
                };

            var errorRate = 0;
            if (statsByNode.TryGetValue(node.Id, out var stats) && stats.Total > 0)
            {
                var rate = (double)stats.Errors / stats.Total;
                errorRate = rate switch
                {
                    0 => 20,
                    < 0.01 => 15,
                    < 0.05 => 10,
                    _ => 0,
                };
            }
            else
            {
                errorRate = 20; // no batches = assume healthy
            }

            var heartbeat = node.LastHeartbeatAt is null ? 0
                : (now - node.LastHeartbeatAt.Value).TotalMinutes switch
                {
                    < 5 => 10,
                    < 30 => 5,
                    _ => 0,
                };

            var score = conn + lag + errorRate + heartbeat;
            return new NodeHealthScore(
                node.Id, node.Name, score, NodeHealthScore.ComputeGrade(score),
                conn, lag, errorRate, heartbeat, now);
        }).ToList();
    }

    public async Task<NodeHealthScore?> GetScoreAsync(int nodeId, CancellationToken ct = default)
    {
        var all = await GetScoresAsync(ct);
        return all.FirstOrDefault(s => s.NodeId == nodeId);
    }
}
```

- [ ] **Step 7: Register in DI**

Find the service registration location (likely `Program.cs` or an extension method). Add:

```csharp
services.AddScoped<IHealthScoringService, HealthScoringService>();
```

- [ ] **Step 8: Run tests — all pass**

```
dotnet test tests/MSOSync.ApiTests -v minimal
```

Expected: `Passed: N+3, Failed: 0`

- [ ] **Step 9: Commit**

```
git add src/MSOSync.Api/Health/NodeHealthScore.cs src/MSOSync.Api/Health/IHealthScoringService.cs src/MSOSync.Api/Health/HealthScoringService.cs tests/MSOSync.ApiTests/Health/HealthScoringServiceTests.cs
git commit -m "feat(2F.3-T1): add NodeHealthScore model + HealthScoringService"
```

---

### Task 2: SloOptions + ISloService + SloService

**Files:**
- Create: `src/MSOSync.Api/Health/SloOptions.cs`
- Create: `src/MSOSync.Api/Health/SloStatus.cs`
- Create: `src/MSOSync.Api/Health/ISloService.cs`
- Create: `src/MSOSync.Api/Health/SloService.cs`
- Create: `tests/MSOSync.ApiTests/Health/SloServiceTests.cs`
- Modify: `src/MSOSync.App/appsettings.json` (add Slo section)

**Interfaces:**
- Consumes: `MSOSyncDbContext` batch history tables (same schema identified in Task 1 Step 1)
- Produces: `SloStatus { DeliveryRate, DeliveryRateTarget, DeliveryRateMet, LatencyP99Ms, LatencyP99TargetMs, LatencyP99Met, WindowStart, WindowEnd }`, `ISloService.GetStatusAsync(ct) : Task<SloStatus>`

- [ ] **Step 1: Create SloOptions**

```csharp
// src/MSOSync.Api/Health/SloOptions.cs
namespace MSOSync.Api.Health;

public sealed class SloOptions
{
    public const string Section = "Slo";

    public double DeliveryRateTarget { get; set; } = 0.999;
    public double LatencyP99TargetMs { get; set; } = 5000;
    public int WindowHours { get; set; } = 24;
}
```

- [ ] **Step 2: Create SloStatus**

```csharp
// src/MSOSync.Api/Health/SloStatus.cs
namespace MSOSync.Api.Health;

public sealed record SloStatus(
    double DeliveryRate,
    double DeliveryRateTarget,
    bool DeliveryRateMet,
    double LatencyP99Ms,
    double LatencyP99TargetMs,
    bool LatencyP99Met,
    DateTime WindowStart,
    DateTime WindowEnd);
```

- [ ] **Step 3: Add Slo section to appsettings.json**

Read `src/MSOSync.App/appsettings.json`. Add:

```json
"Slo": {
  "DeliveryRateTarget": 0.999,
  "LatencyP99TargetMs": 5000,
  "WindowHours": 24
}
```

- [ ] **Step 4: Write failing tests**

```csharp
// tests/MSOSync.ApiTests/Health/SloServiceTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSOSync.Api.Health;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.ApiTests.Health;

public sealed class SloServiceTests : IDisposable
{
    private readonly MSOSyncDbContext _db;
    private readonly IOptions<SloOptions> _opts =
        Options.Create(new SloOptions { DeliveryRateTarget = 0.999, LatencyP99TargetMs = 5000, WindowHours = 24 });

    public SloServiceTests()
    {
        var options = new DbContextOptionsBuilder<MSOSyncDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new MSOSyncDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetStatusAsync_Returns100PctDelivery_WhenAllBatchesSucceed()
    {
        var now = DateTime.UtcNow;
        // Adapt SyncBatch entity and property names to actual schema
        for (var i = 0; i < 10; i++)
        {
            _db.SyncBatches.Add(new SyncBatch
            {
                NodeId = 1,
                Status = "Completed",         // adapt to actual success status value
                CreatedAt = now.AddHours(-1),
                CompletedAt = now.AddHours(-1).AddSeconds(100 + i * 10),
            });
        }
        await _db.SaveChangesAsync();

        var svc = new SloService(_db, _opts);
        var status = await svc.GetStatusAsync();

        status.DeliveryRate.Should().Be(1.0);
        status.DeliveryRateMet.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsDeliveryRateBelowTarget_WhenBatchesFail()
    {
        var now = DateTime.UtcNow;
        _db.SyncBatches.Add(new SyncBatch { NodeId = 1, Status = "Failed", CreatedAt = now.AddHours(-1), CompletedAt = now.AddHours(-1).AddSeconds(200) });
        for (var i = 0; i < 999; i++)
            _db.SyncBatches.Add(new SyncBatch { NodeId = 1, Status = "Completed", CreatedAt = now.AddHours(-1), CompletedAt = now.AddHours(-1).AddSeconds(100 + i) });
        await _db.SaveChangesAsync();

        var svc = new SloService(_db, _opts);
        var status = await svc.GetStatusAsync();

        status.DeliveryRate.Should().BeApproximately(0.999, 0.0001);
        status.DeliveryRateMet.Should().BeFalse(); // 999/1000 < 0.999 target (just barely fails)
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsWindowBounds()
    {
        var svc = new SloService(_db, _opts);
        var status = await svc.GetStatusAsync();

        (status.WindowEnd - status.WindowStart).TotalHours.Should().BeApproximately(24, 0.1);
    }
}
```

Note: adapt `SyncBatch` entity and property names (`Status`, `CreatedAt`, `CompletedAt`, `NodeId`) to the actual schema found in Task 1 Step 1.

- [ ] **Step 5: Run tests — expect compile failure**

```
dotnet test tests/MSOSync.ApiTests -v minimal 2>&1 | head -10
```

- [ ] **Step 6: Create ISloService**

```csharp
// src/MSOSync.Api/Health/ISloService.cs
namespace MSOSync.Api.Health;

public interface ISloService
{
    Task<SloStatus> GetStatusAsync(CancellationToken ct = default);
}
```

- [ ] **Step 7: Implement SloService**

Adapt entity names and property names to actual schema. For P99 latency, compute duration as `CompletedAt - CreatedAt` in milliseconds and find the 99th percentile.

```csharp
// src/MSOSync.Api/Health/SloService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSOSync.Persistence;

namespace MSOSync.Api.Health;

internal sealed class SloService(MSOSyncDbContext db, IOptions<SloOptions> options) : ISloService
{
    public async Task<SloStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        var now = DateTime.UtcNow;
        var windowStart = now.AddHours(-opts.WindowHours);

        // Adapt entity name (SyncBatches) and property names to actual schema
        var batches = await db.SyncBatches
            .AsNoTracking()
            .Where(b => b.CreatedAt >= windowStart && b.CompletedAt != null)
            .Select(b => new
            {
                IsSuccess = b.Status == "Completed", // adapt success status value
                DurationMs = EF.Functions.DateDiffMillisecond(b.CreatedAt, b.CompletedAt!.Value),
            })
            .ToListAsync(ct);

        double deliveryRate;
        if (batches.Count == 0)
        {
            deliveryRate = 1.0; // no data = assume SLO met
        }
        else
        {
            deliveryRate = (double)batches.Count(b => b.IsSuccess) / batches.Count;
        }

        // P99 latency
        double p99Ms = 0;
        if (batches.Count > 0)
        {
            var sorted = batches
                .Where(b => b.IsSuccess)
                .Select(b => b.DurationMs)
                .OrderBy(d => d)
                .ToList();

            if (sorted.Count > 0)
            {
                var p99Index = (int)Math.Ceiling(sorted.Count * 0.99) - 1;
                p99Ms = sorted[Math.Max(0, p99Index)];
            }
        }

        return new SloStatus(
            DeliveryRate: deliveryRate,
            DeliveryRateTarget: opts.DeliveryRateTarget,
            DeliveryRateMet: deliveryRate >= opts.DeliveryRateTarget,
            LatencyP99Ms: p99Ms,
            LatencyP99TargetMs: opts.LatencyP99TargetMs,
            LatencyP99Met: p99Ms <= opts.LatencyP99TargetMs,
            WindowStart: windowStart,
            WindowEnd: now);
    }
}
```

Note: `EF.Functions.DateDiffMillisecond` is SQL Server-specific. If using a different provider or InMemory in tests, the InMemory provider doesn't support `DateDiffMillisecond`. For InMemory tests, use `(b.CompletedAt!.Value - b.CreatedAt).TotalMilliseconds` via `.AsEnumerable()` after the filter. To keep the service compatible with both: compute the query in two steps — filter in SQL, then compute duration in memory:

```csharp
var rawBatches = await db.SyncBatches
    .AsNoTracking()
    .Where(b => b.CreatedAt >= windowStart && b.CompletedAt != null)
    .Select(b => new { b.Status, b.CreatedAt, b.CompletedAt })
    .ToListAsync(ct);

var batches = rawBatches.Select(b => new
{
    IsSuccess = b.Status == "Completed",
    DurationMs = (b.CompletedAt!.Value - b.CreatedAt).TotalMilliseconds,
}).ToList();
```

Use this two-step pattern in the implementation to support InMemory in tests.

- [ ] **Step 8: Register in DI**

```csharp
services.AddOptions<SloOptions>()
    .BindConfiguration(SloOptions.Section)
    .ValidateOnStart();
services.AddScoped<ISloService, SloService>();
```

- [ ] **Step 9: Run tests — all pass**

```
dotnet test tests/MSOSync.ApiTests -v minimal
```

Expected: `Passed: N+3, Failed: 0`

- [ ] **Step 10: Commit**

```
git add src/MSOSync.Api/Health/SloOptions.cs src/MSOSync.Api/Health/SloStatus.cs src/MSOSync.Api/Health/ISloService.cs src/MSOSync.Api/Health/SloService.cs tests/MSOSync.ApiTests/Health/SloServiceTests.cs src/MSOSync.App/appsettings.json
git commit -m "feat(2F.3-T2): add SloOptions + ISloService + SloService"
```

---

### Task 3: HealthScoreController + SloController

**Files:**
- Create: `src/MSOSync.Api/Controllers/HealthScoreController.cs`
- Create: `src/MSOSync.Api/Controllers/SloController.cs`
- Create: `tests/MSOSync.ApiTests/Controllers/HealthScoreControllerTests.cs`
- Create: `tests/MSOSync.ApiTests/Controllers/SloControllerTests.cs`

**Interfaces:**
- Consumes: `IHealthScoringService` (Task 1), `ISloService` (Task 2)
- Produces:
  - `GET /api/health/scores` (AdminOnly) — all node health scores
  - `GET /api/health/scores/{nodeId}` (AdminOnly) — single node score
  - `GET /api/slo/status` (AdminOnly) — current SLO status

- [ ] **Step 1: Write failing tests**

```csharp
// tests/MSOSync.ApiTests/Controllers/HealthScoreControllerTests.cs
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MSOSync.Api.Controllers;
using MSOSync.Api.Health;
using Xunit;

namespace MSOSync.ApiTests.Controllers;

public sealed class HealthScoreControllerTests
{
    private readonly Mock<IHealthScoringService> _svc = new();
    private readonly HealthScoreController _controller;

    public HealthScoreControllerTests()
        => _controller = new HealthScoreController(_svc.Object);

    [Fact]
    public async Task GetScores_ReturnsOkWithScores()
    {
        _svc.Setup(s => s.GetScoresAsync(default))
            .ReturnsAsync([new NodeHealthScore(1, "Node A", 95, "A", 40, 30, 20, 5, DateTime.UtcNow)]);

        var result = await _controller.GetScores();

        result.Result.Should().BeOfType<OkObjectResult>();
        var body = (IEnumerable<NodeHealthScore>)((OkObjectResult)result.Result!).Value!;
        body.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetScore_ReturnsNotFound_WhenNodeMissing()
    {
        _svc.Setup(s => s.GetScoreAsync(999, default)).ReturnsAsync((NodeHealthScore?)null);

        var result = await _controller.GetScore(999);

        result.Result.Should().BeOfType<NotFoundResult>();
    }
}
```

```csharp
// tests/MSOSync.ApiTests/Controllers/SloControllerTests.cs
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MSOSync.Api.Controllers;
using MSOSync.Api.Health;
using Xunit;

namespace MSOSync.ApiTests.Controllers;

public sealed class SloControllerTests
{
    private readonly Mock<ISloService> _svc = new();
    private readonly SloController _controller;

    public SloControllerTests() => _controller = new SloController(_svc.Object);

    [Fact]
    public async Task GetStatus_ReturnsOkWithSloStatus()
    {
        var now = DateTime.UtcNow;
        _svc.Setup(s => s.GetStatusAsync(default))
            .ReturnsAsync(new SloStatus(1.0, 0.999, true, 1200, 5000, true, now.AddHours(-24), now));

        var result = await _controller.GetStatus();

        result.Result.Should().BeOfType<OkObjectResult>();
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test tests/MSOSync.ApiTests -v minimal 2>&1 | head -10
```

- [ ] **Step 3: Implement controllers**

```csharp
// src/MSOSync.Api/Controllers/HealthScoreController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Health;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/health")]
[Authorize(Policy = "AdminOnly")]
public sealed class HealthScoreController(IHealthScoringService scoringService) : ControllerBase
{
    [HttpGet("scores")]
    public async Task<ActionResult<IEnumerable<NodeHealthScore>>> GetScores(CancellationToken ct = default)
        => Ok(await scoringService.GetScoresAsync(ct));

    [HttpGet("scores/{nodeId:int}")]
    public async Task<ActionResult<NodeHealthScore>> GetScore(int nodeId, CancellationToken ct = default)
    {
        var score = await scoringService.GetScoreAsync(nodeId, ct);
        return score is null ? NotFound() : Ok(score);
    }
}
```

```csharp
// src/MSOSync.Api/Controllers/SloController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Health;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/slo")]
[Authorize(Policy = "AdminOnly")]
public sealed class SloController(ISloService sloService) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<SloStatus>> GetStatus(CancellationToken ct = default)
        => Ok(await sloService.GetStatusAsync(ct));
}
```

- [ ] **Step 4: Run tests — all pass**

```
dotnet test tests/MSOSync.ApiTests -v minimal
```

Expected: `Passed: N+3, Failed: 0`

- [ ] **Step 5: Build**

```
dotnet build src/MSOSync.App/MSOSync.App.csproj 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```
git add src/MSOSync.Api/Controllers/HealthScoreController.cs src/MSOSync.Api/Controllers/SloController.cs tests/MSOSync.ApiTests/Controllers/HealthScoreControllerTests.cs tests/MSOSync.ApiTests/Controllers/SloControllerTests.cs
git commit -m "feat(2F.3-T3): add HealthScoreController + SloController"
```

---

### Task 4: Full test suite verification + minor docs

**Files:**
- No new source files

- [ ] **Step 1: Run full test suite**

```
dotnet test --filter "FullyQualifiedName!~IntegrationTest" -v minimal 2>&1 | tail -15
```

Expected: `Passed: N, Failed: 0`. Fix any regressions before proceeding.

- [ ] **Step 2: Verify build**

```
dotnet build src/MSOSync.App/MSOSync.App.csproj 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit if any fixes made**

If regressions were found and fixed in Step 1:

```
git add <files-changed>
git commit -m "fix(2F.3-T4): resolve regressions from health scoring integration"
```

If no fixes needed, no commit required.

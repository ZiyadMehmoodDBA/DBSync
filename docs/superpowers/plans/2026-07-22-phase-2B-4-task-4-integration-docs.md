# Task 4 — Integration Tests + Docs

Part of [Phase 2B.4 Master Plan](2026-07-22-phase-2B-4-master.md). Must run after Tasks 1–3. Adds integration tests for all three new endpoints and updates architecture docs.

## Prerequisites

Tasks 1, 2, and 3 must be complete. The following endpoints must exist:
- `GET /api/v1/cluster/health-trends?window=6h`
- `GET /api/v1/cluster/recovery`
- `GET /api/v1/cluster/diagnostics`

## Files

**Create:**
- `tests/MSOSync.IntegrationTests/Operations/ClusterHealthTrendsApiTests.cs`
- `tests/MSOSync.IntegrationTests/Operations/RecoveryDashboardApiTests.cs`
- `tests/MSOSync.IntegrationTests/Operations/ClusterDiagnosticsApiTests.cs`

**Modify:**
- `docs/architecture/service-responsibility-map.md` — add Phase 2B.4 section
- `docs/architecture/test-infrastructure.md` — update counts

---

- [ ] **Step 1: Examine existing Operations integration test fixture**

Read `tests/MSOSync.IntegrationTests/Operations/ClusterApiTests.cs` to confirm:
- Collection name: `"Operations"`
- Fixture type: `OperationsFixture`
- How `AdminClientAsync()`, `ViewerClientAsync()`, `CreateClient()` are called
- How `fixture.Services.CreateScope()` is used to seed data

The new tests use the same collection and fixture.

- [ ] **Step 2: Create health trends integration tests**

Create `tests/MSOSync.IntegrationTests/Operations/ClusterHealthTrendsApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Operations;

[Collection("Operations")]
public sealed class ClusterHealthTrendsApiTests(OperationsFixture fixture)
{
    private const string Base = "api/v1/cluster/health-trends";

    [Fact]
    public async Task GetHealthTrends_DefaultWindow_Returns200WithCorrectShape()
    {
        var client = await fixture.AdminClientAsync();

        var resp = await client.GetAsync(Base);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("window").GetString().Should().Be("6h");
        body.GetProperty("bucketCount").GetInt32().Should().Be(12);
        body.GetProperty("buckets").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("nodeProbeStats").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Theory]
    [InlineData("1h",  12)]
    [InlineData("6h",  12)]
    [InlineData("24h", 12)]
    [InlineData("7d",  14)]
    public async Task GetHealthTrends_AllWindows_Return200WithCorrectBucketCount(string window, int expectedCount)
    {
        var client = await fixture.AdminClientAsync();

        var resp = await client.GetAsync($"{Base}?window={window}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("window").GetString().Should().Be(window);
        body.GetProperty("bucketCount").GetInt32().Should().Be(expectedCount);
        body.GetProperty("buckets").GetArrayLength().Should().Be(expectedCount);
    }

    [Fact]
    public async Task GetHealthTrends_InvalidWindow_Returns400()
    {
        var client = await fixture.AdminClientAsync();

        var resp = await client.GetAsync($"{Base}?window=99h");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetHealthTrends_NoToken_Returns401()
    {
        var client = fixture.CreateClient();

        var resp = await client.GetAsync(Base);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 3: Create recovery dashboard integration tests**

Create `tests/MSOSync.IntegrationTests/Operations/RecoveryDashboardApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Operations;

[Collection("Operations")]
public sealed class RecoveryDashboardApiTests(OperationsFixture fixture)
{
    private const string Base = "api/v1/cluster/recovery";

    [Fact]
    public async Task GetRecovery_Returns200WithCorrectShape()
    {
        var client = await fixture.AdminClientAsync();

        var resp = await client.GetAsync(Base);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("summary").ValueKind.Should().Be(JsonValueKind.Object);
        body.GetProperty("activeRecoveries").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("recentCompletedRecoveries").ValueKind.Should().Be(JsonValueKind.Array);

        var summary = body.GetProperty("summary");
        summary.GetProperty("activeCount").ValueKind.Should().Be(JsonValueKind.Number);
        summary.GetProperty("completedLast30Days").ValueKind.Should().Be(JsonValueKind.Number);
    }

    [Fact]
    public async Task GetRecovery_SeededRecoveryNode_AppearsInActiveRecoveries()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var nodeId = $"int-rec-{Guid.NewGuid():N}";
        db.Nodes.Add(new SyncNode
        {
            NodeId         = nodeId,
            GroupId        = "int-test",
            SyncUrl        = "http://int-rec.local",
            LifecycleState = NodeLifecycleState.Recovery,
        });
        db.NodeLifecycleHistories.Add(new SyncNodeLifecycleHistory
        {
            NodeId     = nodeId,
            FromState  = NodeLifecycleState.Active,
            ToState    = NodeLifecycleState.Recovery,
            Trigger    = LifecycleTrigger.System,
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-20),
        });
        await db.SaveChangesAsync();

        var client = await fixture.AdminClientAsync();
        var resp   = await client.GetAsync(Base);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var active = body.GetProperty("activeRecoveries");
        active.GetArrayLength().Should().BeGreaterThan(0);

        var nodeIds = active.EnumerateArray()
            .Select(e => e.GetProperty("nodeId").GetString())
            .ToList();
        nodeIds.Should().Contain(nodeId);
    }

    [Fact]
    public async Task GetRecovery_CompletedRecovery_AppearsInCompleted()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var nodeId        = $"int-done-{Guid.NewGuid():N}";
        var recoveryStart = DateTimeOffset.UtcNow.AddDays(-3);
        var restored      = recoveryStart.AddMinutes(30);

        db.Nodes.Add(new SyncNode { NodeId = nodeId, GroupId = "int-test", SyncUrl = "http://int-done.local", LifecycleState = NodeLifecycleState.Active });
        db.NodeLifecycleHistories.AddRange(
            new SyncNodeLifecycleHistory { NodeId = nodeId, FromState = NodeLifecycleState.Active,   ToState = NodeLifecycleState.Recovery, Trigger = LifecycleTrigger.System, OccurredAt = recoveryStart },
            new SyncNodeLifecycleHistory { NodeId = nodeId, FromState = NodeLifecycleState.Recovery, ToState = NodeLifecycleState.Active,   Trigger = LifecycleTrigger.Manual, OccurredAt = restored      });
        await db.SaveChangesAsync();

        var client = await fixture.AdminClientAsync();
        var resp   = await client.GetAsync(Base);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body      = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var completed = body.GetProperty("recentCompletedRecoveries");
        var nodeIds   = completed.EnumerateArray()
            .Select(e => e.GetProperty("nodeId").GetString())
            .ToList();
        nodeIds.Should().Contain(nodeId);
    }

    [Fact]
    public async Task GetRecovery_NoToken_Returns401()
    {
        var client = fixture.CreateClient();
        var resp   = await client.GetAsync(Base);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 4: Create diagnostics integration tests**

Create `tests/MSOSync.IntegrationTests/Operations/ClusterDiagnosticsApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Operations;

[Collection("Operations")]
public sealed class ClusterDiagnosticsApiTests(OperationsFixture fixture)
{
    private const string Base = "api/v1/cluster/diagnostics";

    [Fact]
    public async Task GetDiagnostics_Returns200WithAllThreeSubLists()
    {
        var client = await fixture.AdminClientAsync();

        var resp = await client.GetAsync(Base);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("runtimeStats").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("activeLocks").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("slowOperations").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetDiagnostics_EmptyDb_ReturnsEmptyListsNot500()
    {
        // Integration DB may have data; just verify 200 and shape
        var client = await fixture.AdminClientAsync();
        var resp   = await client.GetAsync(Base);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        // All sub-lists must be JSON arrays (even if empty)
        body.GetProperty("runtimeStats").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("activeLocks").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("slowOperations").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetDiagnostics_StaleLock_HasIsStaleTrueInResponse()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lockName = $"stale-{Guid.NewGuid():N}";
        db.Set<SyncLock>().Add(new SyncLock
        {
            LockName  = lockName,
            LockOwner = "int-test-worker",
            LockTime  = DateTime.UtcNow.AddMinutes(-15),
            Scope     = LockScope.Platform,
        });
        await db.SaveChangesAsync();

        var client = await fixture.AdminClientAsync();
        var resp   = await client.GetAsync(Base);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body  = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var locks = body.GetProperty("activeLocks").EnumerateArray().ToList();
        var stale = locks.FirstOrDefault(l => l.GetProperty("lockName").GetString() == lockName);
        stale.ValueKind.Should().NotBe(JsonValueKind.Undefined, "seeded stale lock should appear");
        stale.GetProperty("isStale").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetDiagnostics_NoToken_Returns401()
    {
        var client = fixture.CreateClient();
        var resp   = await client.GetAsync(Base);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 5: Run full unit test suite**

```powershell
dotnet test D:\MSOSync\MSOSync.sln --filter "FullyQualifiedName~MSOSync.MetadataTests|FullyQualifiedName~MSOSync.SchedulerTests|FullyQualifiedName~MSOSync.AppTests|FullyQualifiedName~MSOSync.SecurityTests|FullyQualifiedName~MSOSync.ConfigurationTests|FullyQualifiedName~MSOSync.EngineTests|FullyQualifiedName~MSOSync.PluginTests|FullyQualifiedName~MSOSync.SdkTests|FullyQualifiedName~MSOSync.Tests|FullyQualifiedName~MSOSync.TransportTests|FullyQualifiedName~MSOSync.ArchTests"
```

Expected: all pass. Fix any regressions before continuing.

- [ ] **Step 6: Update service-responsibility-map.md**

Open `docs/architecture/service-responsibility-map.md`. Append after the `### Phase 2B.3 — Advanced Operations Analytics` section:

```markdown
### Phase 2B.4 — Cluster Health, Recovery, Diagnostics

| Service | Interface | Project | Notes |
|---|---|---|---|
| `ClusterHealthTrendService` | `IClusterHealthTrendService` | MSOSync.Metadata | Aggregates `SyncNodeConnectivityHistory` into time-bucketed trends; window params: 1h/6h/24h/7d; per-node UptimePct and ConsecutiveProbeFailures |
| `RecoveryDashboardQueryService` | `IRecoveryDashboardQueryService` | MSOSync.Metadata | Correlates `SyncNodeLifecycleHistory` + `SyncNodeConnectivityHistory` + `SyncOperation/ReplayItem` for RTO tracking; active and completed recoveries |
| `ClusterDiagnosticsQueryService` | `IClusterDiagnosticsQueryService` | MSOSync.Metadata | Queries `SyncRuntimeStats` (TOP 50, desc), `SyncLock` (active + stale detection), `SyncOperation` Running/Pending (TOP 20, asc) |

New controller endpoints (on existing `ClusterController`):
- `GET /api/v1/cluster/health-trends?window=&nodeId=` → `ClusterController`
- `GET /api/v1/cluster/recovery` → `ClusterController`
- `GET /api/v1/cluster/diagnostics` → `ClusterController`
```

- [ ] **Step 7: Update test-infrastructure.md**

Open `docs/architecture/test-infrastructure.md`.

Update the table row for `MSOSync.MetadataTests` — add ~20 new unit tests (8 ClusterHealthTrend + 6 RecoveryDashboard + 6 ClusterDiagnostics):

Change:
```
| `MSOSync.MetadataTests` | Unit | Domain services, query services, DTOs, validators | 58 | ~531 |
```
To:
```
| `MSOSync.MetadataTests` | Unit | Domain services, query services, DTOs, validators | 61 | ~551 |
```

Update the row for `MSOSync.IntegrationTests` — add 12 new tests (4 health + 4 recovery + 4 diagnostics):

Change:
```
| `MSOSync.IntegrationTests` | Integration | Full API + DB (Testcontainers / WebApplicationFactory) | 58 | ~404 |
```
To:
```
| `MSOSync.IntegrationTests` | Integration | Full API + DB (Testcontainers / WebApplicationFactory) | 61 | ~416 |
```

Update the footnote at the bottom of the counts section to reflect Phase 2B.4:

Change:
```
Counts as of Phase 2B.3 (2026-07-22). New since 2B.2: ...
```
To:
```
Counts as of Phase 2B.4 (2026-07-22). New since 2B.3: `ClusterHealthTrendServiceTests`
(~8 tests), `RecoveryDashboardQueryServiceTests` (~6 tests), `ClusterDiagnosticsQueryServiceTests`
(~6 tests), `ClusterHealthTrendsApiTests` (4 tests, integration), `RecoveryDashboardApiTests`
(4 tests, integration), `ClusterDiagnosticsApiTests` (4 tests, integration).
Frontend: 14 new Vitest tests across 3 component test files.
Full-solution exit-gate run: all unit assemblies green; `MSOSync.IntegrationTests` environmental
failures (2A-014 + 2A-023) remain accepted.
```

Also update the `Critical Path Coverage` table — add a new row:
```
| Cluster analytics (health trends, recovery, diagnostics) | Unit (`MSOSync.MetadataTests`) + Integration | Bucket aggregation, RTO computation, stale lock detection |
```

- [ ] **Step 8: Final build verification**

```powershell
dotnet build D:\MSOSync\MSOSync.sln
cd D:\MSOSync\src\MSOSync.Frontend; npm run build
```

Expected: 0 errors on both.

- [ ] **Step 9: Commit**

```powershell
git add `
  tests/MSOSync.IntegrationTests/Operations/ClusterHealthTrendsApiTests.cs `
  tests/MSOSync.IntegrationTests/Operations/RecoveryDashboardApiTests.cs `
  tests/MSOSync.IntegrationTests/Operations/ClusterDiagnosticsApiTests.cs `
  docs/architecture/service-responsibility-map.md `
  docs/architecture/test-infrastructure.md

git commit -m "test(2B.4-T4): integration tests + docs for all three 2B.4 modules"
```

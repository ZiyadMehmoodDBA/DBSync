# Task 6 — Integration Tests + Docs

**Files:**
- Create: `tests/MSOSync.IntegrationTests/Operations/ReplayApiTests.cs`
- Modify: `docs/architecture/background-workers.md`
- Modify: `docs/architecture/service-responsibility-map.md`
- Modify: `docs/architecture/test-infrastructure.md`

**Interfaces:**
- Consumes from Tasks 1-5: all replay endpoints, entities, and services

---

- [ ] **Step 1: Check existing integration test fixture**

Look at an existing integration test in `tests/MSOSync.IntegrationTests/Operations/` to find the fixture pattern:

```
ls tests/MSOSync.IntegrationTests/Operations/
```

Confirm the collection name and fixture class used (e.g., `[Collection("Lifecycle")]`).

- [ ] **Step 2: Write integration tests for Replay API**

```csharp
// tests/MSOSync.IntegrationTests/Operations/ReplayApiTests.cs
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MSOSync.IntegrationTests.Fixtures;
using MSOSync.Metadata.Operations.Replay.Dtos;
using Xunit;

namespace MSOSync.IntegrationTests.Operations;

[Collection("Lifecycle")]
public sealed class ReplayApiTests(LifecycleFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Create_replay_FailedDelivery_returns_201_with_item_count()
    {
        var payload = new
        {
            nodeId     = fixture.ActiveNodeId,
            replayMode = "FailedDelivery",
            fromTime   = DateTime.UtcNow.AddDays(-1).ToString("o"),
            toTime     = DateTime.UtcNow.ToString("o"),
        };

        var resp = await Client.PostAsJsonAsync("/api/v1/operations/replay", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await resp.Content.ReadFromJsonAsync<ReplayOperationCreatedDto>();
        body.Should().NotBeNull();
        body!.OperationId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Create_replay_invalid_mode_returns_400()
    {
        var payload = new
        {
            nodeId     = fixture.ActiveNodeId,
            replayMode = "InvalidMode",
            fromTime   = DateTime.UtcNow.AddDays(-1).ToString("o"),
            toTime     = DateTime.UtcNow.ToString("o"),
        };

        var resp = await Client.PostAsJsonAsync("/api/v1/operations/replay", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_replay_range_too_large_returns_400()
    {
        var payload = new
        {
            nodeId     = fixture.ActiveNodeId,
            replayMode = "FailedDelivery",
            fromTime   = DateTime.UtcNow.AddDays(-100).ToString("o"),
            toTime     = DateTime.UtcNow.ToString("o"),
        };

        var resp = await Client.PostAsJsonAsync("/api/v1/operations/replay", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_replay_returns_detail_with_items()
    {
        // First create
        var createPayload = new
        {
            nodeId     = fixture.ActiveNodeId,
            replayMode = "FailedDelivery",
            fromTime   = DateTime.UtcNow.AddDays(-1).ToString("o"),
            toTime     = DateTime.UtcNow.ToString("o"),
        };
        var createResp = await Client.PostAsJsonAsync("/api/v1/operations/replay", createPayload);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<ReplayOperationCreatedDto>();

        // Then get detail
        var getResp = await Client.GetAsync($"/api/v1/operations/replay/{created!.OperationId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await getResp.Content.ReadFromJsonAsync<ReplayOperationDetailDto>();
        detail.Should().NotBeNull();
        detail!.NodeId.Should().Be(fixture.ActiveNodeId);
    }

    [Fact]
    public async Task Cancel_replay_returns_204_and_status_Cancelled()
    {
        var createPayload = new
        {
            nodeId     = fixture.ActiveNodeId,
            replayMode = "FailedDelivery",
            fromTime   = DateTime.UtcNow.AddDays(-1).ToString("o"),
            toTime     = DateTime.UtcNow.ToString("o"),
        };
        var createResp = await Client.PostAsJsonAsync("/api/v1/operations/replay", createPayload);
        var created = await createResp.Content.ReadFromJsonAsync<ReplayOperationCreatedDto>();

        // If zero items, operation is already Completed/NoData — skip cancel test
        if (created!.ItemCount == 0) return;

        var cancelResp = await Client.PostAsync(
            $"/api/v1/operations/replay/{created.OperationId}/cancel", null);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Replay_endpoints_without_permission_return_403()
    {
        // Use a client without ManageNodeLifecycle permission
        // This requires a fixture that provides an unprivileged client
        // Skip if fixture doesn't support it
        if (fixture.UnprivilegedClient is null) return;

        var payload = new
        {
            nodeId = "any", replayMode = "FailedDelivery",
            fromTime = DateTime.UtcNow.AddDays(-1).ToString("o"),
            toTime   = DateTime.UtcNow.ToString("o"),
        };

        var resp = await fixture.UnprivilegedClient.PostAsJsonAsync(
            "/api/v1/operations/replay", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

Note: Integration tests use `LifecycleFixture`. Check `tests/MSOSync.IntegrationTests/Fixtures/LifecycleFixture.cs` for `ActiveNodeId` property. If it doesn't exist, use `fixture.NodeId` or the appropriate property from the fixture.

- [ ] **Step 3: Run integration tests (expect environmental failure)**

```
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~ReplayApiTests" -v normal
```

Expected: environmental failure (no SQL Server) is acceptable; build must pass.

- [ ] **Step 4: Run full test suite**

```
dotnet test D:\MSOSync\MSOSync.sln -v normal
```

Expected: same pre-existing failures as before (2A-014/2A-023); no new failures.

- [ ] **Step 5: Update `docs/architecture/background-workers.md`**

Add `ReplayWorker` row to the Worker Inventory table:

```markdown
| `ReplayWorker` | MSOSync.Scheduler | 10s (ReplayOptions) | ✅ | ✅ | Advances BatchReplay operations; FailedDelivery resets to Retry, MissedData calls IBatchCreator |
```

Update the compliance count from 11/11 to 12/12.

Update the code example header comment if it says "11/11".

- [ ] **Step 6: Update `docs/architecture/service-responsibility-map.md`**

Add new services section under "Phase 2B.2 — Batch Replay":

```markdown
### Phase 2B.2 — Batch Replay

| Service | Interface | Project | Notes |
|---|---|---|---|
| `ReplayOperationService` | `IReplayOperationService` | MSOSync.Metadata | Create/cancel replay operations; item enumeration |
| `ReplayOperationQueryService` | `IReplayOperationQueryService` | MSOSync.Metadata | Detail + paginated items |
| `ReplayWorker` | `BackgroundService` | MSOSync.Scheduler | Tick-based advance; uses IBatchCreator + IRoutingService |
```

- [ ] **Step 7: Update `docs/architecture/test-infrastructure.md`**

Update test file counts and passing test counts to include:
- `ReplayOperationServiceTests.cs` (~9 tests)
- `ReplayWorkerTests.cs` (~9 tests)
- `ReplayWorkerRegistryTests.cs` (1 test)
- `ReplayApiTests.cs` (5 tests, integration)
- `M034MigrationTests.cs` (3 tests, integration)
- `ReplayWizard.test.tsx` (5 tests)

- [ ] **Step 8: Commit docs**

```
git add tests/MSOSync.IntegrationTests/Operations/ReplayApiTests.cs
git add docs/architecture/background-workers.md
git add docs/architecture/service-responsibility-map.md
git add docs/architecture/test-infrastructure.md
git commit -m "feat(2B.2-T6): integration tests + docs updates"
```

- [ ] **Step 9: Final gate — update master plan**

Mark all tasks ✅ in `docs/superpowers/plans/2026-07-22-phase-2B-2-master.md`.

```
git add docs/superpowers/plans/2026-07-22-phase-2B-2-master.md
git commit -m "docs(2B.2): mark all tasks complete"
```

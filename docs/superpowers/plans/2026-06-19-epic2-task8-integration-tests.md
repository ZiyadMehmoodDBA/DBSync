# Epic 2 / Task 8: Integration Tests

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Write `DatabaseFixture` (Testcontainers SQL Server, one container per test class) and 11 integration tests covering connectivity, schema creation, seed data, idempotency, FK integrity, and all 4 query objects.

**Architecture:** `IClassFixture<DatabaseFixture>` spins up SQL Server 2022 in Docker, runs `MigrateAsync()` once, then all test methods share the database. Tests that insert data use unique IDs to avoid cross-test contamination.

**Tech Stack:** Testcontainers.MsSql 4.4.0 / xUnit 2.9.3 / FluentAssertions 6.12.2 / EF Core 9.0.0

## Global Constraints

- `MSSql.ContainerBuilder.Build()` — no custom image configuration needed; Testcontainers defaults to `mcr.microsoft.com/mssql/server:2022-latest`
- `MigrateAsync()` called once in `InitializeAsync()` — not per-test
- `AsNoTracking()` on all query objects (already built in) — tests verify results without tracking overhead
- `DbUpdateException` expected on FK violation test — EF wraps `SqlException` in `DbUpdateException`
- Clean up inserted test data in each test (use unique node/batch IDs per test; clean with `Remove` + `SaveChangesAsync`)
- dotnet PATH:
  ```powershell
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```

---

**Files:**
- Create: `tests/MSOSync.IntegrationTests/DatabaseFixture.cs`
- Create: `tests/MSOSync.IntegrationTests/PersistenceTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` (Task 4), all migrations M001–M008 (Tasks 5–6), all query objects (Task 7)
- Produces: 11 passing integration tests

---

- [ ] **Step 1: Write `DatabaseFixture.cs`**

```csharp
// tests/MSOSync.IntegrationTests/DatabaseFixture.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using Testcontainers.MsSql;

namespace MSOSync.IntegrationTests;

public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();

    public AppDbContext Db { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options;

        Db = new AppDbContext(opts);
        await Db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Db.DisposeAsync();
        await _container.DisposeAsync();
    }
}
```

- [ ] **Step 2: Write the failing test stubs (verify they compile but fail)**

```csharp
// tests/MSOSync.IntegrationTests/PersistenceTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Queries;
using Xunit;

namespace MSOSync.IntegrationTests;

public sealed class PersistenceTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task CanConnect()
    {
        var result = await fixture.Db.Database.CanConnectAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task SchemaCreated_All23TablesExist()
    {
        var count = await fixture.Db.Database
            .SqlQuery<int>($"SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'msosync'")
            .SingleAsync();
        count.Should().Be(23);
    }

    [Fact]
    public async Task SeedData_RolesPresent()
    {
        var roles = await fixture.Db.Roles.AsNoTracking().ToListAsync();
        roles.Should().HaveCount(3);
        roles.Select(r => r.RoleName).Should().BeEquivalentTo(new[] { "ADMIN", "OPERATOR", "VIEWER" });
    }

    [Fact]
    public async Task SeedData_DefaultChannelPresent()
    {
        var channel = await fixture.Db.Channels.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChannelId == "config");
        channel.Should().NotBeNull();
        channel!.Priority.Should().Be(100);
    }

    [Fact]
    public async Task SeedData_ParametersPresent()
    {
        var count = await fixture.Db.Parameters.AsNoTracking().CountAsync();
        count.Should().Be(6);

        var interval = await fixture.Db.Parameters.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ParameterName == "sync.interval.seconds");
        interval.Should().NotBeNull();
        interval!.ParameterValue.Should().Be("900");
    }

    [Fact]
    public async Task MigrationIdempotency_SecondMigrateAsyncNoException()
    {
        await fixture.Db.Database.MigrateAsync();
        var roleCount = await fixture.Db.Roles.AsNoTracking().CountAsync();
        roleCount.Should().Be(3);
    }

    [Fact]
    public async Task ForeignKeyIntegrity_BatchErrorRequiresValidBatch()
    {
        fixture.Db.BatchErrors.Add(new SyncBatchError
        {
            BatchId = 999_999_999L,
            RetryCount = 0
        });

        var act = async () => await fixture.Db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        fixture.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task QueryObjects_GetOfflineNodes_ReturnsStaleRegisteredNodes()
    {
        var nodeId = $"offline-test-{Guid.NewGuid():N}";
        var node = new SyncNode
        {
            NodeId = nodeId,
            GroupId = "test-group",
            SyncUrl = "http://test:8080",
            Status = "REGISTERED",
            LastHeartbeat = DateTime.UtcNow.AddMinutes(-120)
        };
        fixture.Db.Nodes.Add(node);
        await fixture.Db.SaveChangesAsync();

        try
        {
            var query = new GetOfflineNodesQuery(fixture.Db);
            var results = await query.ExecuteAsync(thresholdMinutes: 60);
            results.Should().Contain(n => n.NodeId == nodeId);
        }
        finally
        {
            fixture.Db.Nodes.Remove(node);
            await fixture.Db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task QueryObjects_GetPendingBatches_ReturnsNewAndRetryOnly()
    {
        var channelId = $"ch-{Guid.NewGuid():N[..8]}";
        var nodeId = $"node-{Guid.NewGuid():N[..8]}";

        var newBatch = new SyncOutgoingBatch
        {
            BatchSequence = 1, NodeId = nodeId, ChannelId = channelId,
            Status = 0  // NEW
        };
        var ackedBatch = new SyncOutgoingBatch
        {
            BatchSequence = 2, NodeId = nodeId, ChannelId = channelId,
            Status = 2  // ACKED
        };
        fixture.Db.OutgoingBatches.AddRange(newBatch, ackedBatch);
        await fixture.Db.SaveChangesAsync();

        try
        {
            var query = new GetPendingBatchesQuery(fixture.Db);
            var results = await query.ExecuteAsync(nodeId, channelId);
            results.Should().ContainSingle(b => b.Status == 0);
            results.Should().NotContain(b => b.Status == 2);
        }
        finally
        {
            fixture.Db.OutgoingBatches.RemoveRange(newBatch, ackedBatch);
            await fixture.Db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task QueryObjects_GetRetryCandidates_ReturnsEligibleErrorBatches()
    {
        var nodeId = $"node-{Guid.NewGuid():N[..8]}";
        var channelId = "config";
        var eligible = new SyncOutgoingBatch
        {
            BatchSequence = 10, NodeId = nodeId, ChannelId = channelId,
            Status = 3,  // ERROR
            RetryCount = 1,
            NextRetryTime = DateTime.UtcNow.AddMinutes(-5)
        };
        fixture.Db.OutgoingBatches.Add(eligible);
        await fixture.Db.SaveChangesAsync();

        try
        {
            var query = new GetRetryCandidatesQuery(fixture.Db);
            var results = await query.ExecuteAsync(maxRetries: 3);
            results.Should().Contain(b => b.BatchId == eligible.BatchId);
        }
        finally
        {
            fixture.Db.OutgoingBatches.Remove(eligible);
            await fixture.Db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task QueryObjects_GetEventQueueDepth_CountsUnprocessedPerChannel()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var channelA = $"ch-a-{suffix}";
        var channelB = $"ch-b-{suffix}";

        var events = new[]
        {
            MakeEvent(channelA), MakeEvent(channelA), MakeEvent(channelA),
            MakeEvent(channelB)
        };
        fixture.Db.DataEvents.AddRange(events);
        await fixture.Db.SaveChangesAsync();

        try
        {
            var query = new GetEventQueueDepthQuery(fixture.Db);
            var depth = await query.ExecuteAsync();
            depth[channelA].Should().Be(3);
            depth[channelB].Should().Be(1);
        }
        finally
        {
            fixture.Db.DataEvents.RemoveRange(events);
            await fixture.Db.SaveChangesAsync();
        }
    }

    private static SyncDataEvent MakeEvent(string channelId) => new()
    {
        TriggerId = "t1",
        SourceNodeId = "hub",
        ChannelId = channelId,
        EventType = 'I',
        TableName = "test_table",
        CreateTime = DateTime.UtcNow,
        IsProcessed = false
    };
}
```

- [ ] **Step 3: Run tests — expect them to fail (no DB yet)**

```powershell
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet test tests\MSOSync.IntegrationTests\MSOSync.IntegrationTests.csproj --no-build -v n
```

Expected: Build error or test failures — tests require Docker and all prior tasks complete. Confirm compilation succeeds.

- [ ] **Step 4: Ensure Docker is running, then run all integration tests**

```powershell
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet test tests\MSOSync.IntegrationTests\MSOSync.IntegrationTests.csproj -v n
```

Expected:
```
Passed!  - Failed: 0, Passed: 11, Skipped: 0, Total: 11
```

If any test fails, check:
- Docker Desktop is running
- The migration files are present and compile
- The `channelId` truncation in `GetPendingBatches` test — if `Guid.NewGuid().ToString("N")[..8]` causes compile error, replace with `Guid.NewGuid().ToString("N").Substring(0, 8)`

- [ ] **Step 5: Commit**

```powershell
git add tests/MSOSync.IntegrationTests/DatabaseFixture.cs
git add tests/MSOSync.IntegrationTests/PersistenceTests.cs
git commit -m "test(persistence): add 11 integration tests for Epic 2"
```

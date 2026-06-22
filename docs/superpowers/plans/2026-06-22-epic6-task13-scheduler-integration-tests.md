# Task 13: SchedulerRecovery Sending Recovery + Integration Tests

**Part of:** Epic 6 Transport Layer
**Spec:** `docs/superpowers/specs/2026-06-22-epic6-transport-design.md` § 4 (SchedulerRecovery), § 9 (integration tests)
**Depends on:** Tasks 1–12

**Files:**
- Modify: `src/MSOSync.Scheduler/SchedulerRecovery.cs` — add sent_time filter to Phase 1
- Create: `tests/MSOSync.IntegrationTests/Transport/TransportFixture.cs`
- Create: `tests/MSOSync.IntegrationTests/Transport/TransportTests.cs`

---

## Part A: SchedulerRecovery — Sending Recovery with sent_time filter

- [ ] **Step 1: Update SchedulerRecovery Phase 1**

Task 2 set Phase 1 to recover ALL `Sending` batches on startup. Now add the spec's `sent_time < now - 5 min` filter so that briefly-in-flight batches from a previous tick are not immediately errored. Replace Phase 1 in `src/MSOSync.Scheduler/SchedulerRecovery.cs`:

```csharp
        // 1. Sending → Error (crash during PUSH — only batches stuck > 5 min)
        var staleThreshold = now.AddMinutes(-5);
        var sendingBatches = await db.OutgoingBatches
            .Where(b => b.Status == (byte)BatchStatus.Sending
                     && b.SentTime != null
                     && b.SentTime < staleThreshold)
            .ToListAsync(ct);

        var sendingRecovered = 0;
        foreach (var b in sendingBatches)
        {
            if (await stateMachine.MoveToErrorAsync(b.BatchId, ct))
            {
                sendingRecovered++;
                logger.LogInformation("Recovery {Reason}: Batch {BatchId} Sending→Error (stale {SentTime})",
                    RecoveryReason.Restart, b.BatchId, b.SentTime);
            }
        }
```

The full `StartAsync` after this edit:
1. Phase 1: Sending (sent_time < now-5min) → Error
2. Phase 2: Overdue Retry → requeue (unchanged from Task 2)
3. Phase 3: Stale New (createTime < now-10min) → Retry (unchanged from Task 2)

- [ ] **Step 2: Build to verify**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
dotnet test tests/MSOSync.EngineTests -c Debug
```

Expected: zero warnings, all tests pass.

- [ ] **Step 3: Commit**

```pwsh
git add src/MSOSync.Scheduler/SchedulerRecovery.cs
git commit -m "feat(epic6): SchedulerRecovery Phase 1 filters Sending batches by sent_time < now-5min"
```

---

## Part B: Integration Tests

Integration tests run against a real SQL Server instance via Testcontainers. They require Docker.

- [ ] **Step 4: Add Testcontainers to IntegrationTests project**

Verify `tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj` includes:
```xml
<PackageReference Include="Testcontainers.MsSql" Version="4.4.0" />
```

Also add Transport project reference if not present:
```xml
<ProjectReference Include="..\..\src\MSOSync.Transport\MSOSync.Transport.csproj" />
<ProjectReference Include="..\..\src\MSOSync.Batch\MSOSync.Batch.csproj" />
<ProjectReference Include="..\..\src\MSOSync.Metadata\MSOSync.Metadata.csproj" />
```

Also add Topology if PullJob tests need it:
```xml
<ProjectReference Include="..\..\src\MSOSync.Topology\MSOSync.Topology.csproj" />
```

- [ ] **Step 5: Create TransportFixture**

Check if an existing `IntegrationFixture` or `EngineFixture` exists that can be reused as base. If so, create a collection sharing the container. Otherwise create a fresh fixture.

Create `tests/MSOSync.IntegrationTests/Transport/TransportFixture.cs`:

```csharp
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.App;
using MSOSync.Persistence;
using Testcontainers.MsSql;
using Xunit;

namespace MSOSync.IntegrationTests.Transport;

public sealed class TransportFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();
    private WebApplicationFactory<Program>? _factory;

    public WebApplicationFactory<Program> Factory => _factory!;
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Replace DB with container connection
                    var desc = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (desc != null) services.Remove(desc);

                    services.AddDbContext<AppDbContext>(opts =>
                        opts.UseSqlServer(ConnectionString));

                    // Node identity for tests
                    services.Configure<MSOSync.Common.NodeProperties>(opts =>
                    {
                        opts.NodeId    = "test-node";
                        opts.GroupId   = "test";
                        opts.SyncUrl   = "http://localhost";
                        // NodeToken set via header override in tests
                    });
                });
            });

        // Run migrations
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory != null) await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("Transport")]
public sealed class TransportCollection : ICollectionFixture<TransportFixture> { }
```

- [ ] **Step 6: Create TransportTests**

Create `tests/MSOSync.IntegrationTests/Transport/TransportTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Batch;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Transport;
using MSOSync.Transport.Payloads;
using Xunit;

namespace MSOSync.IntegrationTests.Transport;

[Collection("Transport")]
public sealed class TransportTests(TransportFixture fixture)
{
    private readonly GzipCompressionService _compression = new();

    private static readonly JsonSerializerOptions JsonOpts =
        new(TransportJsonContext.Default.Options);

    private HttpClient CreateClient()
    {
        var client = fixture.Factory.CreateClient();
        // Authenticate as source node using X-Node-Token
        // In tests, NodeTokenAuthMiddleware accepts the token configured in Node:NodeToken
        // Override: set token that matches the test configuration
        client.DefaultRequestHeaders.Add("X-Node-Token", "test-token");
        client.DefaultRequestHeaders.Add("X-Node-Id", "source-node");
        return client;
    }

    private AppDbContext CreateDbContext()
    {
        var scope = fixture.Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    // ── Test 1: Duplicate push is ignored ────────────────────────────────────

    [Fact]
    public async Task DuplicatePush_SecondCallIgnored_OneIncomingBatchRow()
    {
        await using var db = CreateDbContext();

        // Seed: create source node
        if (!await db.Nodes.AnyAsync(n => n.NodeId == "source-node"))
        {
            db.Nodes.Add(new SyncNode
            {
                NodeId = "source-node", GroupId = "test", SyncUrl = "http://source",
                Status = "APPROVED", SyncEnabled = true, TransportMode = TransportMode.Pull
            });
            await db.SaveChangesAsync();
        }

        var payload = new BatchPayload(
            BatchId:       2001,
            BatchSequence: 5,
            ChannelId:     "default",
            SourceNodeId:  "source-node",
            TargetNodeId:  "test-node",
            RowCount:      3,
            Events:        [new EventPayload(1, "trig1", "INSERT", "dbo.orders", null, null, null)]);

        var client = CreateClient();

        // First push
        var r1 = await PushBatch(client, payload);
        r1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second push (duplicate)
        var r2 = await PushBatch(client, payload);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);

        // Only one IncomingBatch row
        await using var db2 = CreateDbContext();
        var count = await db2.IncomingBatches.CountAsync(b => b.BatchSequence == 5 && b.SourceNodeId == "source-node");
        count.Should().Be(1);
    }

    // ── Test 2: Duplicate ACK is ignored ─────────────────────────────────────

    [Fact]
    public async Task DuplicateAck_ThreeCalls_BatchStaysAcknowledged()
    {
        await using var db = CreateDbContext();

        // Seed: create an outgoing batch in New status
        var batch = new SyncOutgoingBatch
        {
            BatchSequence = 10, NodeId = "test-node", ChannelId = "default",
            Status = (byte)BatchStatus.New, RowCount = 1
        };
        db.OutgoingBatches.Add(batch);
        await db.SaveChangesAsync();

        var ack = new AckPayload(batch.BatchId, 10, "test-node", true, null, DateTimeOffset.UtcNow);
        var client = CreateClient();

        // Three ACK calls
        for (var i = 0; i < 3; i++)
        {
            var r = await client.PostAsJsonAsync("/api/v1/sync/ack", ack);
            r.StatusCode.Should().Be(HttpStatusCode.OK, $"call {i + 1}");
        }

        await using var db2 = CreateDbContext();
        var updated = await db2.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Acknowledged);
    }

    // ── Test 3: Pull with no batches returns 204 ─────────────────────────────

    [Fact]
    public async Task Pull_NoBatch_Returns204()
    {
        var req    = new PullRequest("test-node", "empty-channel", 0);
        var client = CreateClient();
        var r      = await client.PostAsJsonAsync("/api/v1/sync/pull", req);
        r.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Test 4: Sequence gap returns 409 ─────────────────────────────────────

    [Fact]
    public async Task SequenceGap_Returns409_WithCode()
    {
        await using var db = CreateDbContext();

        // Seed: source node + two incoming batches (seq 1 and 2)
        if (!await db.Nodes.AnyAsync(n => n.NodeId == "gap-source"))
        {
            db.Nodes.Add(new SyncNode
            {
                NodeId = "gap-source", GroupId = "test", SyncUrl = "http://gap-source",
                Status = "APPROVED", SyncEnabled = true, TransportMode = TransportMode.Pull
            });
        }
        db.IncomingBatches.Add(new SyncIncomingBatch
        {
            BatchId = 3001, NodeId = "test-node", ChannelId = "gap-ch",
            SourceNodeId = "gap-source", BatchSequence = 1, ReceivedTime = DateTime.UtcNow, RowCount = 1
        });
        db.IncomingBatches.Add(new SyncIncomingBatch
        {
            BatchId = 3002, NodeId = "test-node", ChannelId = "gap-ch",
            SourceNodeId = "gap-source", BatchSequence = 2, ReceivedTime = DateTime.UtcNow, RowCount = 1
        });
        await db.SaveChangesAsync();

        // Push batch with seq=4 (gap: seq=3 missing)
        var payload = new BatchPayload(
            3004, 4, "gap-ch", "gap-source", "test-node", 1,
            [new EventPayload(100, "t", "INSERT", "t", null, null, null)]);

        var client = CreateClient();
        var r      = await PushBatch(client, payload);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await r.Content.ReadAsStringAsync();
        body.Should().Contain("SEQUENCE_GAP");
    }

    // ── Test 5: SchedulerRecovery moves stale Sending → Error ────────────────

    [Fact]
    public async Task SchedulerRecovery_StaleSending_MovesToError()
    {
        await using var db = CreateDbContext();

        var staleBatch = new SyncOutgoingBatch
        {
            BatchSequence = 20,
            NodeId        = "test-node",
            ChannelId     = "default",
            Status        = (byte)BatchStatus.Sending,
            SentTime      = DateTime.UtcNow.AddMinutes(-15)  // 15 min ago — stale
        };
        db.OutgoingBatches.Add(staleBatch);
        await db.SaveChangesAsync();

        // Run SchedulerRecovery directly
        using var scope       = fixture.Factory.Services.CreateScope();
        var recovery          = scope.ServiceProvider.GetRequiredService<MSOSync.Scheduler.SchedulerRecovery>();
        await recovery.StartAsync(default);

        await using var db2 = CreateDbContext();
        var updated = await db2.OutgoingBatches.FindAsync(staleBatch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Error);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> PushBatch(HttpClient client, BatchPayload payload)
    {
        var json      = JsonSerializer.Serialize(payload, JsonOpts);
        var bytes     = Encoding.UTF8.GetBytes(json);
        var gzipped   = _compression.Compress(bytes);
        var content   = new ByteArrayContent(gzipped);
        content.Headers.ContentType     = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        content.Headers.ContentEncoding.Add("gzip");
        return await client.PostAsync("/api/v1/sync/push", content);
    }
}
```

- [ ] **Step 7: Run all tests**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
dotnet test tests/MSOSync.TransportTests -c Debug
dotnet test tests/MSOSync.EngineTests -c Debug
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~Transport" -c Debug
```

Expected: all tests pass. Integration tests require Docker. If Docker is unavailable, skip with `--filter "Category!=Docker"`.

- [ ] **Step 8: Final commit**

```pwsh
git add src/MSOSync.Scheduler/SchedulerRecovery.cs
git add tests/MSOSync.IntegrationTests/Transport/TransportFixture.cs
git add tests/MSOSync.IntegrationTests/Transport/TransportTests.cs
git add tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj
git commit -m "feat(epic6): SchedulerRecovery stale Sending filter + Transport integration tests"
```

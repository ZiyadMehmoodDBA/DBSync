# Task 10: Integration tests — EngineCollection

**Part of:** [Epic 5 master plan](2026-06-22-epic5-event-capture.md)

**Files:**
- Create: `tests/MSOSync.IntegrationTests/Engine/EngineFixture.cs`
- Create: `tests/MSOSync.IntegrationTests/Engine/EngineTests.cs`
- Modify: `tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj`

**Interfaces:**
- Consumes (all produced by Tasks 1–8):
  - `AppDbContext`, `SyncOutgoingBatch`, `SyncDataEvent`, `SyncDataEventBatch`, `SyncChannel`, `SyncRouter`, `SyncTrigger`, `SyncTriggerRouter`, `SyncNodeGroup`, `SyncNode`
  - `ITriggerInstallationService`, `ITriggerDriftDetector`
  - `IBatchStateMachine`, `IBatchCreator`, `BatchStatus`
  - `IEventReader`
  - `IRoutingService`
  - `SyncEngine`
  - `AddTriggerEngine`, `AddEventServices`, `AddRoutingServices`, `AddBatchPipeline`, `AddSyncEngine`
- Produces: `EngineFixture`, `EngineCollection`, `EngineTests` (9 test cases)

---

- [ ] **Step 1: Update `MSOSync.IntegrationTests.csproj`**

Add project references for Epic 5 modules (App already transitively pulls most, but explicit is cleaner):

```xml
<!-- tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Testcontainers.MsSql" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.Persistence\MSOSync.Persistence.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.App\MSOSync.App.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Metadata\MSOSync.Metadata.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Api\MSOSync.Api.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Trigger\MSOSync.Trigger.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Event\MSOSync.Event.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Routing\MSOSync.Routing.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Batch\MSOSync.Batch.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Engine\MSOSync.Engine.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `EngineFixture.cs`**

Mirrors `MetadataFixture` pattern: Testcontainers SQL Server + `WebApplicationFactory<Program>` override + seed data. Adds Epic 5 DI registrations and seeds a test table, trigger, router, and node.

```csharp
// tests/MSOSync.IntegrationTests/Engine/EngineFixture.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MSOSync.Api.Controllers.Auth;
using MSOSync.Api.Exceptions;
using MSOSync.App;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Engine;
using MSOSync.Event;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Routing;
using MSOSync.Security;
using MSOSync.Trigger;
using Testcontainers.MsSql;
using Xunit;

namespace MSOSync.IntegrationTests.Engine;

public sealed class EngineFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";
    public const string NodeId    = "hub";
    public const string ChannelId = "default";
    public const string TriggerId = "t-engine-1";
    public const string GroupId   = "default";
    public const string RouterId  = "r-engine-1";
    public const string TestTable = "msosync.sync_test_source";

    private string? _connStr;

    public AppDbContext CreateDbContext()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_connStr!)
            .Options;
        return new AppDbContext(opts);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var testBuilder = WebApplication.CreateBuilder();
        testBuilder.WebHost.UseTestServer();

        testBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = _connStr,
            ["Jwt:Secret"]          = JwtSecret,
            ["Node:Id"]             = NodeId,
            ["Sync:IntervalSeconds"] = "30",
        });

        testBuilder.Environment.EnvironmentName = "Test";

        testBuilder.Services.AddEndpointsApiExplorer();
        testBuilder.Services.AddPersistence(testBuilder.Configuration);
        testBuilder.Services.AddSecurity(testBuilder.Configuration);
        testBuilder.Services.AddHttpContextAccessor();
        testBuilder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();
        testBuilder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        testBuilder.Services.AddProblemDetails();
        testBuilder.Services.AddMetadata(testBuilder.Configuration);
        testBuilder.Services.AddSingleton<IClock, SystemClock>();
        testBuilder.Services.AddTriggerEngine(testBuilder.Configuration);
        testBuilder.Services.AddEventServices();
        testBuilder.Services.AddRoutingServices();
        testBuilder.Services.AddBatchPipeline(testBuilder.Configuration);
        testBuilder.Services.AddSyncEngine(testBuilder.Configuration);

        testBuilder.Services.AddControllers()
            .AddApplicationPart(typeof(AuthController).Assembly);

        var app = testBuilder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.Start();

        return app;
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connStr = _container.GetConnectionString();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        // Roles
        foreach (var role in new[] { "ADMIN", "OPERATOR", "VIEWER" })
        {
            if (!await db.Roles.AnyAsync(r => r.RoleName == role))
                db.Roles.Add(new SyncRole { RoleName = role });
        }
        await db.SaveChangesAsync();

        // Node group
        if (!await db.NodeGroups.AnyAsync(g => g.GroupId == GroupId))
            db.NodeGroups.Add(new SyncNodeGroup { GroupId = GroupId, GroupName = "Default Group" });
        await db.SaveChangesAsync();

        // Node
        if (!await db.Nodes.AnyAsync(n => n.NodeId == NodeId))
            db.Nodes.Add(new SyncNode
            {
                NodeId = NodeId, GroupId = GroupId,
                SyncUrl = "http://hub:8080", Status = "REGISTERED",
                LastHeartbeat = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        // Channel
        if (!await db.Channels.AnyAsync(c => c.ChannelId == ChannelId))
            db.Channels.Add(new SyncChannel
            {
                ChannelId = ChannelId, Priority = 1,
                BatchSize = 1000, MaxBatchToSend = 10, MaxDataSize = 1048576L
            });
        await db.SaveChangesAsync();

        // Router
        if (!await db.Routers.AnyAsync(r => r.RouterId == RouterId))
            db.Routers.Add(new SyncRouter
            {
                RouterId = RouterId, GroupId = GroupId, ChannelId = ChannelId
            });
        await db.SaveChangesAsync();

        // Trigger
        if (!await db.Triggers.AnyAsync(t => t.TriggerId == TriggerId))
            db.Triggers.Add(new SyncTrigger
            {
                TriggerId = TriggerId, ChannelId = ChannelId,
                SourceTable = TestTable, IsActive = true
            });
        await db.SaveChangesAsync();

        // TriggerRouter
        if (!await db.TriggerRouters.AnyAsync(tr => tr.TriggerId == TriggerId && tr.RouterId == RouterId))
            db.TriggerRouters.Add(new SyncTriggerRouter { TriggerId = TriggerId, RouterId = RouterId });
        await db.SaveChangesAsync();

        // Create test source table in DB
        await db.Database.ExecuteSqlRawAsync($"""
            IF OBJECT_ID(N'{TestTable}', N'U') IS NULL
            CREATE TABLE {TestTable} (
                id   INT IDENTITY(1,1) PRIMARY KEY,
                name NVARCHAR(100) NOT NULL
            )
            """);
    }

    public new async Task DisposeAsync()
    {
        await _container.StopAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("Engine")]
public sealed class EngineCollection : ICollectionFixture<EngineFixture> { }
```

- [ ] **Step 3: Create `EngineTests.cs`**

Nine tests covering the full pipeline end-to-end on a real SQL Server container:

```csharp
// tests/MSOSync.IntegrationTests/Engine/EngineTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Batch;
using MSOSync.Engine;
using MSOSync.Event;
using MSOSync.Persistence.Entities;
using MSOSync.Trigger;
using Xunit;

namespace MSOSync.IntegrationTests.Engine;

[Collection("Engine")]
public sealed class EngineTests(EngineFixture fixture)
{
    private IServiceScope Scope() => fixture.Services.CreateScope();

    // ── Trigger Installation ────────────────────────────────────────────────

    [Fact]
    public async Task TriggerInstall_CreatesRealTriggerInDatabase()
    {
        await using var scope = Scope();
        var svc = scope.ServiceProvider.GetRequiredService<ITriggerInstallationService>();
        var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await svc.RebuildAsync(EngineFixture.TriggerId);

        var exists = await db.Database
            .SqlQuery<int>($"""
                SELECT COUNT(1) AS Value
                FROM sys.triggers t
                WHERE t.name = N'msosync__{EngineFixture.TriggerId}'
                """)
            .SingleAsync();

        exists.Should().Be(1);
    }

    [Fact]
    public async Task TriggerDrift_AfterInstall_ReturnsNoDrift()
    {
        await using var scope = Scope();
        var svc      = scope.ServiceProvider.GetRequiredService<ITriggerInstallationService>();
        var detector = scope.ServiceProvider.GetRequiredService<ITriggerDriftDetector>();

        await svc.RebuildAsync(EngineFixture.TriggerId);
        var result = await detector.VerifyAsync(EngineFixture.TriggerId);

        result.HasDrift.Should().BeFalse();
    }

    [Fact]
    public async Task TriggerDrift_AfterManualAlteration_DetectsDrift()
    {
        await using var scope = Scope();
        var svc      = scope.ServiceProvider.GetRequiredService<ITriggerInstallationService>();
        var detector = scope.ServiceProvider.GetRequiredService<ITriggerDriftDetector>();
        var db       = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await svc.RebuildAsync(EngineFixture.TriggerId);

        // Alter the trigger body manually to simulate out-of-band change
        await db.Database.ExecuteSqlRawAsync($"""
            CREATE OR ALTER TRIGGER [msosync__{EngineFixture.TriggerId}]
            ON {EngineFixture.TestTable} AFTER INSERT, UPDATE, DELETE
            AS BEGIN SELECT 1 END
            """);

        var result = await detector.VerifyAsync(EngineFixture.TriggerId);
        result.HasDrift.Should().BeTrue();
    }

    // ── Event Reading ───────────────────────────────────────────────────────

    [Fact]
    public async Task EventReader_NoEvents_ReturnsEmptyList()
    {
        await using var scope = Scope();
        var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IEventReader>();

        // Ensure no unprocessed events for our trigger
        await db.DataEvents
            .Where(e => e.TriggerId == EngineFixture.TriggerId && !e.IsProcessed)
            .ExecuteDeleteAsync();

        var events = await reader.ReadAsync(100);
        events.Should().NotContain(e => e.TriggerId == EngineFixture.TriggerId);
    }

    [Fact]
    public async Task EventReader_SeedsAndReads_ReturnsUnprocessedEvents()
    {
        await using var scope = Scope();
        var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IEventReader>();

        var evt = new SyncDataEvent
        {
            TriggerId    = EngineFixture.TriggerId,
            ChannelId    = EngineFixture.ChannelId,
            SourceNodeId = EngineFixture.NodeId,
            EventType    = 'I',
            TableName    = EngineFixture.TestTable,
            CreateTime   = DateTime.UtcNow,
            IsProcessed  = false,
        };
        db.DataEvents.Add(evt);
        await db.SaveChangesAsync();

        try
        {
            var events = await reader.ReadAsync(100);
            events.Should().Contain(e => e.EventId == evt.EventId);
        }
        finally
        {
            await db.DataEvents.Where(e => e.EventId == evt.EventId).ExecuteDeleteAsync();
        }
    }

    // ── Routing ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RoutingService_Resolve_ReturnsNodeForSeededTrigger()
    {
        await using var scope = Scope();
        var routing = scope.ServiceProvider.GetRequiredService<IRoutingService>();

        var nodes = await routing.ResolveAsync(EngineFixture.TriggerId);

        nodes.Should().Contain(EngineFixture.NodeId);
    }

    // ── Batch Creation ──────────────────────────────────────────────────────

    [Fact]
    public async Task BatchCreator_CreatesAndPersistsBatch_ForSeededEvent()
    {
        await using var scope = Scope();
        var db      = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var creator = scope.ServiceProvider.GetRequiredService<IBatchCreator>();
        var routing = scope.ServiceProvider.GetRequiredService<IRoutingService>();

        var evt = new SyncDataEvent
        {
            TriggerId    = EngineFixture.TriggerId,
            ChannelId    = EngineFixture.ChannelId,
            SourceNodeId = EngineFixture.NodeId,
            EventType    = 'I',
            TableName    = EngineFixture.TestTable,
            CreateTime   = DateTime.UtcNow,
            IsProcessed  = false,
        };
        db.DataEvents.Add(evt);
        await db.SaveChangesAsync();

        try
        {
            var routes = new Dictionary<long, IReadOnlyList<string>>
            {
                [evt.EventId] = await routing.ResolveAsync(EngineFixture.TriggerId)
            };

            var batches = await creator.CreateBatchesAsync([evt], routes);

            batches.Should().HaveCount(1);
            var batch = batches[0];
            batch.NodeId.Should().Be(EngineFixture.NodeId);
            batch.ChannelId.Should().Be(EngineFixture.ChannelId);
            batch.Status.Should().Be((byte)BatchStatus.New);
            batch.RowCount.Should().Be(1);

            // Verify event marked processed
            var refreshed = await db.DataEvents.AsNoTracking()
                .FirstAsync(e => e.EventId == evt.EventId);
            refreshed.IsProcessed.Should().BeTrue();
        }
        finally
        {
            await db.DataEvents.Where(e => e.EventId == evt.EventId).ExecuteDeleteAsync();
            await db.OutgoingBatches.Where(b => b.NodeId == EngineFixture.NodeId && b.ChannelId == EngineFixture.ChannelId).ExecuteDeleteAsync();
        }
    }

    // ── Full SyncEngine Cycle ───────────────────────────────────────────────

    [Fact]
    public async Task SyncEngine_NoEvents_CompletesWithoutCreatingBatches()
    {
        await using var scope = Scope();
        var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<SyncEngine>();

        await db.DataEvents
            .Where(e => e.TriggerId == EngineFixture.TriggerId && !e.IsProcessed)
            .ExecuteDeleteAsync();

        var batchCountBefore = await db.OutgoingBatches.CountAsync();

        await engine.RunAsync();

        var batchCountAfter = await db.OutgoingBatches.CountAsync();
        batchCountAfter.Should().Be(batchCountBefore);
    }

    [Fact]
    public async Task SyncEngine_WithEvent_CreatesNewBatch_AndMarksEventProcessed()
    {
        await using var scope = Scope();
        var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<SyncEngine>();

        var evt = new SyncDataEvent
        {
            TriggerId    = EngineFixture.TriggerId,
            ChannelId    = EngineFixture.ChannelId,
            SourceNodeId = EngineFixture.NodeId,
            EventType    = 'I',
            TableName    = EngineFixture.TestTable,
            CreateTime   = DateTime.UtcNow,
            IsProcessed  = false,
        };
        db.DataEvents.Add(evt);
        await db.SaveChangesAsync();

        try
        {
            await engine.RunAsync();

            var refreshed = await db.DataEvents.AsNoTracking()
                .FirstAsync(e => e.EventId == evt.EventId);
            refreshed.IsProcessed.Should().BeTrue();

            var batchLink = await db.DataEventBatches.AsNoTracking()
                .FirstOrDefaultAsync(l => l.EventId == evt.EventId);
            batchLink.Should().NotBeNull();
        }
        finally
        {
            await db.DataEventBatches.Where(l => l.EventId == evt.EventId).ExecuteDeleteAsync();
            await db.DataEvents.Where(e => e.EventId == evt.EventId).ExecuteDeleteAsync();
        }
    }
}
```

- [ ] **Step 4: Build**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj -c Debug --warnaserror
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Run Engine integration tests**

```pwsh
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~Engine" -c Debug
```

Expected: 9 tests, all green. (Container spin-up takes ~20–30 s on first run.)

- [ ] **Step 6: Commit**

```pwsh
git add tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj `
        tests/MSOSync.IntegrationTests/Engine/EngineFixture.cs `
        tests/MSOSync.IntegrationTests/Engine/EngineTests.cs
git commit -m "test(integration): add EngineCollection — full pipeline integration tests"
```

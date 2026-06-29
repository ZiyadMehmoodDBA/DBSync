# Task 3: MetricsController + DI Wire + Integration Tests

**Part of:** [Epic 9C Plan](2026-06-29-epic9c-metrics-apis.md)

**Goal:** Create `MetricsController`, register `IMetricsQueryService` in DI, create `MetricsFixture` with seeded LocalDB data, and write 7 integration tests. Full test suite must pass.

**Files:**
- Create: `src/MSOSync.Api/Controllers/MetricsController.cs`
- Modify: `src/MSOSync.Metadata/MetadataServiceExtensions.cs`
- Create: `tests/MSOSync.IntegrationTests/Metrics/MetricsFixture.cs`
- Create: `tests/MSOSync.IntegrationTests/Metrics/MetricsTests.cs`

**Interfaces:**
- Consumes (from Task 1): `MetricsSummaryDto`, `NodeMetricsDto`, `ChannelMetricsDto`, `RuntimeMetricsDto`, `MonitorMetricDto` in namespace `MSOSync.Metadata.Metrics`
- Consumes (from Task 2): `IMetricsQueryService` in namespace `MSOSync.Metadata.Metrics`
- Controller base route: `api/v1/metrics`
- All endpoints: `[Authorize(Policy = "ViewerOrAbove")]`

**Pattern reference:** Follow `OperationalReadFixture` exactly:
- `WebApplicationFactory<Program>` + `IAsyncLifetime`
- DB name: `"MSOSyncMetrics_Test"`
- JwtSecret: `"test-jwt-secret-value-at-least-32-chars!"`
- Token field: `body.GetProperty("token").GetString()!` (camelCase JSON)
- DisposeAsync: `SET SINGLE_USER WITH ROLLBACK IMMEDIATE` then `EnsureDeletedAsync`
- Marker class with `[CollectionDefinition("Metrics")]` + `ICollectionFixture<MetricsFixture>`
- Test class with `[Collection("Metrics")]`

**Current state of `MetadataServiceExtensions.cs`** (confirm before editing):
```csharp
// Epic 9A registrations already present:
services.AddSingleton<IErrorSeverityClassifier, ErrorSeverityClassifier>();
services.AddScoped<IEventQueryService, EventQueryService>();
services.AddScoped<IIncomingBatchQueryService, IncomingBatchQueryService>();
services.AddScoped<IBatchErrorQueryService, BatchErrorQueryService>();
services.AddScoped<IValidator<EventFilter>, EventFilterValidator>();
services.AddScoped<IValidator<IncomingBatchFilter>, IncomingBatchFilterValidator>();
services.AddScoped<IValidator<BatchErrorFilter>, BatchErrorFilterValidator>();
// Epic 9B will add ITopologyQueryService here (if 9B merged before 9C)
```

---

- [ ] **Step 1: Create MetricsController.cs**

Create `src/MSOSync.Api/Controllers/MetricsController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.Metrics;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/metrics")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class MetricsController(IMetricsQueryService metrics) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
        => Ok(await metrics.GetSummaryAsync(ct));

    [HttpGet("nodes")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetNodes(CancellationToken ct)
        => Ok(await metrics.GetNodeMetricsAsync(ct));

    [HttpGet("channels")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetChannels(CancellationToken ct)
        => Ok(await metrics.GetChannelMetricsAsync(ct));

    [HttpGet("runtime")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetRuntime(CancellationToken ct)
        => Ok(await metrics.GetRuntimeMetricsAsync(ct));

    [HttpGet("monitors")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetMonitors(
        [FromQuery] string? nodeId,
        [FromQuery] string? metricName,
        CancellationToken ct)
        => Ok(await metrics.GetMonitorMetricsAsync(nodeId, metricName, ct));
}
```

- [ ] **Step 2: Register IMetricsQueryService in MetadataServiceExtensions.cs**

Read `src/MSOSync.Metadata/MetadataServiceExtensions.cs` first. Locate the `AddMetadata` method. Add the following line (and `using MSOSync.Metadata.Metrics;` if the namespace isn't already covered by a top-level using):

```csharp
// Epic 9C — Metrics APIs
services.AddScoped<IMetricsQueryService, MetricsQueryService>();
```

Add it after the existing Epic 9B registration (if present) or after the Epic 9A registrations.

- [ ] **Step 3: Build the full solution**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Create MetricsFixture.cs**

Create `tests/MSOSync.IntegrationTests/Metrics/MetricsFixture.cs`:

```csharp
// tests/MSOSync.IntegrationTests/Metrics/MetricsFixture.cs
using System.Text.Json;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MSOSync.Api.Controllers.Auth;
using MSOSync.Api.Exceptions;
using MSOSync.App;
using MSOSync.Common;
using MSOSync.Metadata;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using MSOSync.Topology;
using System.Net.Http.Json;
using Xunit;

namespace MSOSync.IntegrationTests.Metrics;

public sealed class MetricsFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncMetrics_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public string ViewerUsername { get; } = "viewer-user";
    public string ViewerPassword { get; } = "ViewP@ss1!";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var testBuilder = WebApplication.CreateBuilder();
        testBuilder.WebHost.UseTestServer();

        testBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = ConnStr,
            ["Jwt:Secret"]                          = JwtSecret,
            ["Jwt:Issuer"]                          = "msosync",
            ["Jwt:Audience"]                        = "msosync-dashboard",
            ["Jwt:AccessExpiryMinutes"]             = "60",
            ["RateLimit:LoginPermitLimit"]          = "100",
            ["RateLimit:RefreshPermitLimit"]        = "100",
        });

        testBuilder.Services.AddPersistence(testBuilder.Configuration);
        testBuilder.Services.AddSecurity(testBuilder.Configuration);
        testBuilder.Services.AddMetadata(testBuilder.Configuration);
        testBuilder.Services.AddSingleton<IClock, SystemClock>();
        testBuilder.Services.AddTopologyServices();
        testBuilder.Services.AddHttpContextAccessor();
        testBuilder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();
        testBuilder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        testBuilder.Services.AddProblemDetails();

        testBuilder.Services.AddControllers()
            .AddApplicationPart(typeof(AuthController).Assembly);

        testBuilder.Services.AddFluentValidationAutoValidation();
        testBuilder.Services.AddValidatorsFromAssemblyContaining<AuthController>();

        var app = testBuilder.Build();

        app.UseExceptionHandler();
        app.UseRateLimiter();
        app.UseSecurityHeaders();
        app.UseAuthentication();
        app.UseNodeTokenAuth();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/health", () => Results.Ok(new { status = "UP" }));

        app.Start();
        return app;
    }

    public async Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnStr).Options;
        await using var db = new AppDbContext(opts);
        await db.Database.MigrateAsync();

        // Roles
        if (!await db.Roles.AnyAsync(r => r.RoleName == "VIEWER"))
            db.Roles.Add(new SyncRole { RoleName = "VIEWER" });
        if (!await db.Roles.AnyAsync(r => r.RoleName == "ADMIN"))
            db.Roles.Add(new SyncRole { RoleName = "ADMIN" });
        await db.SaveChangesAsync();

        // Viewer user
        if (!await db.Users.AnyAsync(u => u.Username == ViewerUsername))
        {
            var hasher = new BCryptPasswordHasher();
            var user   = new SyncUser
            {
                Username     = ViewerUsername,
                PasswordHash = hasher.Hash(ViewerPassword),
                Enabled      = true,
                CreatedTime  = DateTime.UtcNow,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var viewerRole = await db.Roles.FirstAsync(r => r.RoleName == "VIEWER");
            db.UserRoles.Add(new SyncUserRole { UserId = user.UserId, RoleId = viewerRole.RoleId });
            await db.SaveChangesAsync();
        }

        await SeedAsync(db);
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Nodes.AnyAsync()) return;

        // Nodes
        db.Nodes.AddRange(
            new SyncNode { NodeId = "hub-1",   GroupId = "group-hub",   SyncUrl = "http://hub-1",   Status = "REGISTERED", ConnectivityStatus = ConnectivityStatus.Reachable  },
            new SyncNode { NodeId = "store-1", GroupId = "group-store", SyncUrl = "http://store-1", Status = "REGISTERED", ConnectivityStatus = ConnectivityStatus.Degraded   });
        await db.SaveChangesAsync();

        // DataEvents (2 pending on ch-default, 1 processed on ch-config)
        db.DataEvents.AddRange(
            new SyncDataEvent { TriggerId = "t1", SourceNodeId = "hub-1", ChannelId = "ch-default", EventType = 'I', TableName = "dbo.Order", CreateTime = DateTime.UtcNow.AddHours(-1), IsProcessed = false },
            new SyncDataEvent { TriggerId = "t1", SourceNodeId = "hub-1", ChannelId = "ch-default", EventType = 'U', TableName = "dbo.Order", CreateTime = DateTime.UtcNow.AddHours(-2), IsProcessed = false },
            new SyncDataEvent { TriggerId = "t2", SourceNodeId = "hub-1", ChannelId = "ch-config",  EventType = 'I', TableName = "dbo.Config", CreateTime = DateTime.UtcNow.AddHours(-1), IsProcessed = true  });
        await db.SaveChangesAsync();

        // IncomingBatches
        db.IncomingBatches.AddRange(
            new SyncIncomingBatch { BatchId = 2001L, NodeId = "hub-1",   SourceNodeId = "hub-1",   ChannelId = "ch-default", BatchSequence = 1L, Status = IncomingBatchStatus.Applied, ReceivedTime = DateTime.UtcNow.AddHours(-3), AppliedTime = DateTime.UtcNow.AddHours(-3).AddMilliseconds(80),  ApplyTimeMs = 80L  },
            new SyncIncomingBatch { BatchId = 2002L, NodeId = "store-1", SourceNodeId = "hub-1",   ChannelId = "ch-default", BatchSequence = 2L, Status = IncomingBatchStatus.Applied, ReceivedTime = DateTime.UtcNow.AddHours(-2), AppliedTime = DateTime.UtcNow.AddHours(-2).AddMilliseconds(120), ApplyTimeMs = 120L },
            new SyncIncomingBatch { BatchId = 2003L, NodeId = "hub-1",   SourceNodeId = "hub-1",   ChannelId = "ch-config",  BatchSequence = 3L, Status = IncomingBatchStatus.New,     ReceivedTime = DateTime.UtcNow });
        await db.SaveChangesAsync();

        // OutgoingBatches (one pending, one acknowledged)
        var ob1 = new SyncOutgoingBatch { BatchSequence = 1L, NodeId = "hub-1",   ChannelId = "ch-default", Status = 0 }; // New = pending
        var ob2 = new SyncOutgoingBatch { BatchSequence = 2L, NodeId = "store-1", ChannelId = "ch-default", Status = 2 }; // Acknowledged
        db.OutgoingBatches.AddRange(ob1, ob2);
        await db.SaveChangesAsync();

        // BatchError linked to ob2 (already acknowledged, but error within 24h)
        db.BatchErrors.Add(new SyncBatchError { BatchId = ob2.BatchId, ErrorMessage = "conflict", CreateTime = DateTime.UtcNow.AddHours(-1) });
        await db.SaveChangesAsync();

        // RuntimeStats (2 snapshots)
        db.RuntimeStats.AddRange(
            new SyncRuntimeStats { HeapUsed = 512_000_000L, HeapMax = 1_024_000_000L, ThreadCount = 20, CpuPercent = 12.5m, GcCount = 100L, GcTimeMs = 200L, UptimeMs = 3_600_000L, CreateTime = DateTime.UtcNow.AddMinutes(-10) },
            new SyncRuntimeStats { HeapUsed = 600_000_000L, HeapMax = 1_024_000_000L, ThreadCount = 22, CpuPercent = 18.0m, GcCount = 110L, GcTimeMs = 220L, UptimeMs = 3_660_000L, CreateTime = DateTime.UtcNow.AddMinutes(-5)  });
        await db.SaveChangesAsync();

        // SyncMonitor rows
        db.Monitors.AddRange(
            new SyncMonitor { NodeId = "hub-1",   MetricName = "cpu",    MetricValue = "12.5", CreateTime = DateTime.UtcNow.AddMinutes(-5)  },
            new SyncMonitor { NodeId = "hub-1",   MetricName = "memory", MetricValue = "512",  CreateTime = DateTime.UtcNow.AddMinutes(-5)  },
            new SyncMonitor { NodeId = "store-1", MetricName = "cpu",    MetricValue = "5.0",  CreateTime = DateTime.UtcNow.AddMinutes(-10) });
        await db.SaveChangesAsync();
    }

    public async Task<string> GetViewerTokenAsync()
    {
        var client = CreateClient();
        var resp   = await client.PostAsJsonAsync("api/v1/auth/login", new
        {
            username = ViewerUsername,
            password = ViewerPassword
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    public new async Task DisposeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnStr).Options;
        await using var db = new AppDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "ALTER DATABASE [MSOSyncMetrics_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await db.Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("Metrics")]
public sealed class MetricsCollection : ICollectionFixture<MetricsFixture> { }
```

- [ ] **Step 5: Create MetricsTests.cs**

Create `tests/MSOSync.IntegrationTests/Metrics/MetricsTests.cs`:

```csharp
// tests/MSOSync.IntegrationTests/Metrics/MetricsTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.Metrics;

[Collection("Metrics")]
public sealed class MetricsTests(MetricsFixture fixture)
{
    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var token  = await fixture.GetViewerTokenAsync();
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task GetSummary_Returns200_WithGeneratedAt()
    {
        var client = await AuthenticatedClientAsync();

        var resp = await client.GetAsync("api/v1/metrics/summary");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("generatedAt").GetDateTime().Should()
            .BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        body.GetProperty("totalNodes").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task GetNodes_Returns200_WithSeededNodes()
    {
        var client = await AuthenticatedClientAsync();

        var resp = await client.GetAsync("api/v1/metrics/nodes");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var nodes = body.EnumerateArray().ToList();
        nodes.Should().HaveCount(2);

        var hub = nodes.Single(n => n.GetProperty("nodeId").GetString() == "hub-1");
        hub.GetProperty("connectivityStatus").GetInt32().Should().Be(1); // Reachable = 1
    }

    [Fact]
    public async Task GetChannels_Returns200_WithPendingEvents()
    {
        var client = await AuthenticatedClientAsync();

        var resp = await client.GetAsync("api/v1/metrics/channels");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var channels = body.EnumerateArray().ToList();
        channels.Should().NotBeEmpty();

        var def = channels.Single(c => c.GetProperty("channelId").GetString() == "ch-default");
        def.GetProperty("pendingEvents").GetInt64().Should().Be(2);
    }

    [Fact]
    public async Task GetRuntime_Returns200_WithRuntimeStats()
    {
        var client = await AuthenticatedClientAsync();

        var resp = await client.GetAsync("api/v1/metrics/runtime");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var rows = body.EnumerateArray().ToList();
        rows.Should().HaveCount(2);
        // Ordered descending by CreateTime — most recent first
        rows[0].GetProperty("heapUsed").GetInt64().Should().Be(600_000_000L);
    }

    [Fact]
    public async Task GetMonitors_Returns200_WithAllRows()
    {
        var client = await AuthenticatedClientAsync();

        var resp = await client.GetAsync("api/v1/metrics/monitors");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.EnumerateArray().Should().HaveCount(3);
    }

    [Fact]
    public async Task GetMonitors_FilteredByNodeIdAndMetricName()
    {
        var client = await AuthenticatedClientAsync();

        var resp = await client.GetAsync("api/v1/metrics/monitors?nodeId=hub-1&metricName=cpu");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var rows = body.EnumerateArray().ToList();
        rows.Should().HaveCount(1);
        rows[0].GetProperty("metricValue").GetString().Should().Be("12.5");
    }

    [Fact]
    public async Task AllEndpoints_Unauthorized_Returns401()
    {
        var client = fixture.CreateClient();

        var summaryResp  = await client.GetAsync("api/v1/metrics/summary");
        var nodesResp    = await client.GetAsync("api/v1/metrics/nodes");
        var channelsResp = await client.GetAsync("api/v1/metrics/channels");
        var runtimeResp  = await client.GetAsync("api/v1/metrics/runtime");
        var monitorsResp = await client.GetAsync("api/v1/metrics/monitors");

        summaryResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        nodesResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        channelsResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        runtimeResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        monitorsResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 6: Run integration tests**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~Metrics" -c Debug -v normal
```

Expected: 7 tests pass, 0 fail.

- [ ] **Step 7: Run full test suite**

```powershell
dotnet test MSOSync.sln -c Debug -v normal
```

Expected: all existing tests still pass, 0 regressions.

- [ ] **Step 8: Build solution one final time**

```powershell
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 9: Commit**

```powershell
git add src/MSOSync.Api/Controllers/MetricsController.cs
git add src/MSOSync.Metadata/MetadataServiceExtensions.cs
git add tests/MSOSync.IntegrationTests/Metrics/MetricsFixture.cs
git add tests/MSOSync.IntegrationTests/Metrics/MetricsTests.cs
git commit -m "feat(9c): add MetricsController + DI wire + integration tests"
```

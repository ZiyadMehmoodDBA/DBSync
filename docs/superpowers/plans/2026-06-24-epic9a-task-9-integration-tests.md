# Task 9: Integration Tests

**Part of:** [Epic 9A Plan](2026-06-24-epic9a-operational-read-apis.md)

**Goal:** Create a shared `OperationalReadFixture` (LocalDB, WebApplicationFactory) and three integration test classes that exercise the full HTTP stack — auth, validation, routing, pagination, and 404 behavior — for all three controllers.

**Files:**
- Create: `tests/MSOSync.IntegrationTests/OperationalRead/OperationalReadFixture.cs`
- Create: `tests/MSOSync.IntegrationTests/OperationalRead/EventsTests.cs`
- Create: `tests/MSOSync.IntegrationTests/OperationalRead/IncomingBatchesTests.cs`
- Create: `tests/MSOSync.IntegrationTests/OperationalRead/BatchErrorsTests.cs`

**Interfaces:**
- Consumes: All controllers from Task 6; DI registrations from Task 7; M015 migration from Task 7
- The fixture follows `UsersFixture.cs` exactly: LocalDB, `WebApplicationFactory<Program>`, `IAsyncLifetime`

**Prerequisites:** Tasks 6 and 7 must be complete — controllers and DI wiring must be in place.

---

- [ ] **Step 1: Create OperationalReadFixture**

Create `tests/MSOSync.IntegrationTests/OperationalRead/OperationalReadFixture.cs`:

**Important:** The `[CollectionDefinition]` marker class must live in the same file (or any file in the same assembly) — it is the glue that lets all three test classes share one fixture instance. Without it, `[Collection("OperationalRead")]` on the test classes has no effect and each class spins up its own fixture.

```csharp
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
using MSOSync.Metadata;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using MSOSync.Topology;
using System.Net.Http.Json;
using Xunit;

namespace MSOSync.IntegrationTests.OperationalRead;

public sealed class OperationalReadFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncOperationalRead_Test;" +
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

        // Seed roles
        if (!await db.Roles.AnyAsync(r => r.RoleName == "VIEWER"))
            db.Roles.Add(new SyncRole { RoleName = "VIEWER" });
        if (!await db.Roles.AnyAsync(r => r.RoleName == "ADMIN"))
            db.Roles.Add(new SyncRole { RoleName = "ADMIN" });
        await db.SaveChangesAsync();

        // Seed viewer user
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

        // Seed source node (required for FK on IncomingBatch)
        if (!await db.Nodes.AnyAsync(n => n.NodeId == "test-node-1"))
        {
            db.Nodes.Add(new SyncNode
            {
                NodeId       = "test-node-1",
                NodeName     = "Test Node 1",
                NodeType     = "SPOKE",
                Status       = "REGISTERED",
                IsEnabled    = true,
                DatabaseType = "SQLSERVER",
                SyncUrl      = "http://test-node-1",
                CreatedTime  = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Seed test data
        await SeedTestDataAsync(db);
    }

    private static async Task SeedTestDataAsync(AppDbContext db)
    {
        // Seed Events (5 total: 3 processed, 2 not)
        if (!await db.DataEvents.AnyAsync())
        {
            var events = new[]
            {
                new SyncDataEvent { TriggerId = "trig-1", SourceNodeId = "test-node-1", ChannelId = "ch-1", EventType = 'I', TableName = "dbo.Product", CreateTime = DateTime.UtcNow.AddHours(-2), IsProcessed = true  },
                new SyncDataEvent { TriggerId = "trig-1", SourceNodeId = "test-node-1", ChannelId = "ch-1", EventType = 'U', TableName = "dbo.Product", CreateTime = DateTime.UtcNow.AddHours(-1), IsProcessed = true  },
                new SyncDataEvent { TriggerId = "trig-2", SourceNodeId = "test-node-1", ChannelId = "ch-1", EventType = 'D', TableName = "dbo.Order",   CreateTime = DateTime.UtcNow,                IsProcessed = true  },
                new SyncDataEvent { TriggerId = "trig-2", SourceNodeId = "test-node-1", ChannelId = "ch-1", EventType = 'I', TableName = "dbo.Order",   CreateTime = DateTime.UtcNow.AddMinutes(-5), IsProcessed = false },
                new SyncDataEvent { TriggerId = "trig-1", SourceNodeId = "test-node-1", ChannelId = "ch-1", EventType = 'U', TableName = "dbo.Product", CreateTime = DateTime.UtcNow.AddMinutes(-1), IsProcessed = false },
            };
            db.DataEvents.AddRange(events);
            await db.SaveChangesAsync();
        }

        // Seed IncomingBatches (3: 1 Applied, 1 Error, 1 New)
        if (!await db.IncomingBatches.AnyAsync())
        {
            db.IncomingBatches.AddRange(
                new SyncIncomingBatch { BatchId = 1001L, NodeId = "test-node-1", SourceNodeId = "test-node-1", ChannelId = "ch-1", Status = IncomingBatchStatus.Applied, BatchSequence = 1L, ReceivedTime = DateTime.UtcNow.AddHours(-2), AppliedTime = DateTime.UtcNow.AddHours(-2).AddMilliseconds(120), ApplyTimeMs = 120L },
                new SyncIncomingBatch { BatchId = 1002L, NodeId = "test-node-1", SourceNodeId = "test-node-1", ChannelId = "ch-1", Status = IncomingBatchStatus.Error,   BatchSequence = 2L, ReceivedTime = DateTime.UtcNow.AddHours(-1) },
                new SyncIncomingBatch { BatchId = 1003L, NodeId = "test-node-1", SourceNodeId = "test-node-1", ChannelId = "ch-1", Status = IncomingBatchStatus.New,     BatchSequence = 3L, ReceivedTime = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        // Seed BatchErrors (4: Info + 2 Warning + Critical; 2 from today, 2 from yesterday)
        if (!await db.BatchErrors.AnyAsync())
        {
            db.BatchErrors.AddRange(
                new SyncBatchError { BatchId = 1001L, ConflictType = "DuplicateKey",   ErrorMessage = "Duplicate",        RetryCount = 0, CreateTime = DateTime.UtcNow.AddDays(-1) },
                new SyncBatchError { BatchId = 1002L, ConflictType = "Timeout",         ErrorMessage = "Timeout occurred", RetryCount = 2, CreateTime = DateTime.UtcNow.AddDays(-1) },
                new SyncBatchError { BatchId = 1002L, ConflictType = "Deadlock",         ErrorMessage = "Deadlock",         RetryCount = 1, CreateTime = DateTime.UtcNow },
                new SyncBatchError { BatchId = 1002L, ConflictType = "MetadataMissing", ErrorMessage = "Missing meta",     RetryCount = 0, CreateTime = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
    }

    public async Task<string> GetViewerTokenAsync()
    {
        var client = CreateClient();
        var resp = await client.PostAsJsonAsync("api/v1/auth/login", new
        {
            username = ViewerUsername,
            password = ViewerPassword
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    public new async Task DisposeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnStr).Options;
        await using var db = new AppDbContext(opts);
        await db.Database.EnsureDeletedAsync();
    }
}

// Registers the fixture for sharing across all [Collection("OperationalRead")] test classes.
// ICollectionFixture<T> (not IClassFixture<T>) — one instance shared, not one per class.
[CollectionDefinition("OperationalRead")]
public sealed class OperationalReadCollection : ICollectionFixture<OperationalReadFixture> { }
```

**Note:** `using System.Text.Json;` is needed for `JsonElement`. Add at top of file.

Full usings list for the fixture file:
```csharp
using System.Text.Json;
using FluentValidation.AspNetCore;
using FluentValidation;
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
using MSOSync.Metadata;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using MSOSync.Topology;
using System.Net.Http.Json;
using Xunit;
```

- [ ] **Step 2: Create EventsTests**

Create `tests/MSOSync.IntegrationTests/OperationalRead/EventsTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Metadata.Common;
using MSOSync.Metadata.Events;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace MSOSync.IntegrationTests.OperationalRead;

[Collection("OperationalRead")]
public sealed class EventsTests(OperationalReadFixture fixture)
{
    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var client = fixture.CreateClient();
        var token  = await fixture.GetViewerTokenAsync();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task GetEvents_ReturnsAllSeeded()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<PagedResult<EventSummaryDto>>(
            "api/v1/events");

        result!.TotalCount.Should().Be(5);
        result.Items.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetEvents_FilterByIsProcessed_ReturnsThree()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<PagedResult<EventSummaryDto>>(
            "api/v1/events?isProcessed=true");

        result!.TotalCount.Should().Be(3);
        result.Items.Should().OnlyContain(e => e.IsProcessed);
    }

    [Fact]
    public async Task GetEvents_InvalidPage_Returns400()
    {
        var client = await AuthenticatedClientAsync();
        var resp   = await client.GetAsync("api/v1/events?page=0");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetEvents_PageSizeTooLarge_Returns400()
    {
        var client = await AuthenticatedClientAsync();
        var resp   = await client.GetAsync("api/v1/events?pageSize=101");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetEventById_Existing_Returns200WithDto()
    {
        var client = await AuthenticatedClientAsync();

        var list = await client.GetFromJsonAsync<PagedResult<EventSummaryDto>>("api/v1/events");
        var id   = list!.Items.First().EventId;

        var resp = await client.GetAsync($"api/v1/events/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<EventDetailDto>();
        dto!.EventId.Should().Be(id);
    }

    [Fact]
    public async Task GetEventById_Missing_Returns404()
    {
        var client = await AuthenticatedClientAsync();
        var resp   = await client.GetAsync("api/v1/events/9999999");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetEvents_NoToken_Returns401()
    {
        var client = fixture.CreateClient();
        var resp   = await client.GetAsync("api/v1/events");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetEvents_ViewerToken_Returns200()
    {
        var client = await AuthenticatedClientAsync();
        var resp   = await client.GetAsync("api/v1/events");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

- [ ] **Step 3: Create IncomingBatchesTests**

Create `tests/MSOSync.IntegrationTests/OperationalRead/IncomingBatchesTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Metadata.Common;
using MSOSync.Metadata.IncomingBatches;
using MSOSync.Persistence;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace MSOSync.IntegrationTests.OperationalRead;

[Collection("OperationalRead")]
public sealed class IncomingBatchesTests(OperationalReadFixture fixture)
{
    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var client = fixture.CreateClient();
        var token  = await fixture.GetViewerTokenAsync();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task GetIncomingBatches_ReturnsAllSeeded()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<PagedResult<IncomingBatchSummaryDto>>(
            "api/v1/incoming-batches");

        result!.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task GetIncomingBatches_FilterByStatus_ReturnsOne()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<PagedResult<IncomingBatchSummaryDto>>(
            "api/v1/incoming-batches?status=Error");

        result!.TotalCount.Should().Be(1);
        result.Items.Single().Status.Should().Be(IncomingBatchStatus.Error);
    }

    [Fact]
    public async Task GetIncomingBatchById_Existing_Returns200()
    {
        var client = await AuthenticatedClientAsync();
        var resp   = await client.GetAsync("api/v1/incoming-batches/1001");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<IncomingBatchDetailDto>();
        dto!.BatchId.Should().Be(1001L);
        dto.Status.Should().Be(IncomingBatchStatus.Applied);
    }

    [Fact]
    public async Task GetIncomingBatchById_Missing_Returns404()
    {
        var client = await AuthenticatedClientAsync();
        var resp   = await client.GetAsync("api/v1/incoming-batches/9999999");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetIncomingBatches_NoToken_Returns401()
    {
        var client = fixture.CreateClient();
        var resp   = await client.GetAsync("api/v1/incoming-batches");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 4: Create BatchErrorsTests**

Create `tests/MSOSync.IntegrationTests/OperationalRead/BatchErrorsTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Metadata.BatchErrors;
using MSOSync.Metadata.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace MSOSync.IntegrationTests.OperationalRead;

[Collection("OperationalRead")]
public sealed class BatchErrorsTests(OperationalReadFixture fixture)
{
    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var client = fixture.CreateClient();
        var token  = await fixture.GetViewerTokenAsync();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task GetBatchErrors_ReturnsAllSeeded()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<PagedResult<BatchErrorSummaryDto>>(
            "api/v1/batch-errors");

        result!.TotalCount.Should().Be(4);
    }

    [Fact]
    public async Task GetBatchErrors_FilterBySeverity_ReturnsWarning()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<PagedResult<BatchErrorSummaryDto>>(
            "api/v1/batch-errors?severity=Warning");

        result!.Items.Should().OnlyContain(e => e.Severity == "Warning");
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetBatchErrorById_Existing_Returns200()
    {
        var client = await AuthenticatedClientAsync();

        var list = await client.GetFromJsonAsync<PagedResult<BatchErrorSummaryDto>>(
            "api/v1/batch-errors");
        var id = list!.Items.First().ErrorId;

        var resp = await client.GetAsync($"api/v1/batch-errors/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetBatchErrorById_Missing_Returns404()
    {
        var client = await AuthenticatedClientAsync();
        var resp   = await client.GetAsync("api/v1/batch-errors/9999999");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetBatchErrorSummary_ReturnsCorrectCounts()
    {
        var client = await AuthenticatedClientAsync();

        var dto = await client.GetFromJsonAsync<BatchErrorSummaryCountDto>(
            "api/v1/batch-errors/summary");

        dto!.Info.Should().Be(1);
        dto.Warning.Should().Be(2);
        dto.Critical.Should().Be(1);
        dto.Total.Should().Be(dto.Info + dto.Warning + dto.Critical);
    }

    [Fact]
    public async Task GetBatchErrorSummary_FilterByBatchId_ScopesCounts()
    {
        var client = await AuthenticatedClientAsync();

        var dto = await client.GetFromJsonAsync<BatchErrorSummaryCountDto>(
            "api/v1/batch-errors/summary?batchId=1002");

        dto!.Total.Should().Be(3);  // batch 1002 has Timeout + Deadlock + MetadataMissing
    }

    [Fact]
    public async Task GetBatchErrorSummary_FilterByFrom_CountsTodayOnly()
    {
        var client = await AuthenticatedClientAsync();
        var from   = DateTime.UtcNow.Date.ToString("O");

        var dto = await client.GetFromJsonAsync<BatchErrorSummaryCountDto>(
            $"api/v1/batch-errors/summary?from={Uri.EscapeDataString(from)}");

        dto!.Total.Should().Be(2);  // Deadlock + MetadataMissing seeded as today
    }

    [Fact]
    public async Task GetBatchErrors_NoToken_Returns401()
    {
        var client = fixture.CreateClient();
        var resp   = await client.GetAsync("api/v1/batch-errors");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 5: Run integration tests**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests\MSOSync.IntegrationTests --filter "FullyQualifiedName~OperationalRead" -c Debug --logger "console;verbosity=normal"
```

Expected: all 20 integration tests PASS.

- [ ] **Step 6: If any test fails, common diagnoses**

**`GetViewerTokenAsync` fails — VIEWER role not seeded:**
Check that `InitializeAsync` seeds `VIEWER` role and assigns it to `viewer-user`. The seed code above handles this.

**`SyncNode FK violation` on IncomingBatch seed:**
The node `test-node-1` must be seeded before `IncomingBatch` rows. The `InitializeAsync` method seeds the node first.

**`IncomingBatchStatus.Error` not deserializing from query string:**
ASP.NET Core enum binding reads by name by default. `?status=Error` should bind to `IncomingBatchStatus.Error` (value 3). If it fails, try `?status=3`.

**`GetBatchErrorSummary_FilterByFrom` date count wrong:**
The seed sets 2 errors to `DateTime.UtcNow.AddDays(-1)` (yesterday) and 2 to `DateTime.UtcNow` (today). Filtering `from = today midnight` should return 2. If timing issues arise in CI, adjust seed to use fixed past dates instead of `DateTime.UtcNow`.

- [ ] **Step 7: Run full integration test suite to check for regressions**

```powershell
dotnet test tests\MSOSync.IntegrationTests -c Debug --logger "console;verbosity=normal"
```

Expected: all existing tests (Users, Heartbeat, etc.) still pass. No regressions.

- [ ] **Step 8: Final build verification**

```powershell
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 9: Commit**

```powershell
git add tests/MSOSync.IntegrationTests/OperationalRead/OperationalReadFixture.cs
git add tests/MSOSync.IntegrationTests/OperationalRead/EventsTests.cs
git add tests/MSOSync.IntegrationTests/OperationalRead/IncomingBatchesTests.cs
git add tests/MSOSync.IntegrationTests/OperationalRead/BatchErrorsTests.cs
git commit -m "feat(9a): add OperationalRead integration tests — events, batches, errors"
```

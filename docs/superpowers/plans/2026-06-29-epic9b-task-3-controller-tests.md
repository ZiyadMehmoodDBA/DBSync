# Task 3: TopologyController + DI Wire + Integration Tests

**Part of:** [Epic 9B Plan](2026-06-29-epic9b-topology-apis.md)

**Goal:** Create `TopologyController`, register `ITopologyQueryService` in `AddMetadata()`, then write and pass 8 integration tests using LocalDB. End with a full suite commit.

**Files:**
- Create: `src/MSOSync.Api/Controllers/TopologyController.cs`
- Modify: `src/MSOSync.Metadata/MetadataServiceExtensions.cs`
- Create: `tests/MSOSync.IntegrationTests/Topology/TopologyFixture.cs`
- Create: `tests/MSOSync.IntegrationTests/Topology/TopologyTests.cs`

**Interfaces:**
- Consumes (from Tasks 1 + 2):
  - `ITopologyQueryService` (interface from Task 2)
  - `TopologyGraphDto`, `TopologyGroupDto`, `TopologyGroupNodeDto`, `TopologySummaryDto` (from Task 1)
  - `NotFoundException` from `MSOSync.Common.Exceptions`
- Produces: 5 HTTP endpoints under `api/v1/topology`

---

- [ ] **Step 1: Create TopologyController.cs**

Create `src/MSOSync.Api/Controllers/TopologyController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Topology;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/topology")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class TopologyController(ITopologyQueryService topology) : ControllerBase
{
    [HttpGet("graph")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetGraph(CancellationToken ct)
        => Ok(await topology.GetTopologyGraphAsync(ct));

    [HttpGet("summary")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
        => Ok(await topology.GetTopologySummaryAsync(ct));

    [HttpGet("groups")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetGroups(CancellationToken ct)
        => Ok(await topology.GetGroupsAsync(ct));

    [HttpGet("groups/{groupId}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetGroup(string groupId, CancellationToken ct)
    {
        var group = await topology.GetGroupAsync(groupId, ct);
        if (group is null) throw new NotFoundException($"Group {groupId} not found.");
        return Ok(group);
    }

    [HttpGet("groups/{groupId}/nodes")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetGroupNodes(string groupId, CancellationToken ct)
        => Ok(await topology.GetGroupNodesAsync(groupId, ct));
}
```

- [ ] **Step 2: Register ITopologyQueryService in MetadataServiceExtensions**

Open `src/MSOSync.Metadata/MetadataServiceExtensions.cs`.

Add using at the top:
```csharp
using MSOSync.Metadata.Topology;
```

Add inside `AddMetadata()` after the Epic 9A registrations:
```csharp
        // Epic 9B — Topology APIs
        services.AddScoped<ITopologyQueryService, TopologyQueryService>();
```

Full file after edit:

```csharp
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Metadata.BatchErrors;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.IncomingBatches;
using MSOSync.Metadata.Interfaces;
using MSOSync.Metadata.Nodes;
using MSOSync.Metadata.Services;
using MSOSync.Metadata.Topology;
using MSOSync.Metadata.Users;

namespace MSOSync.Metadata;

public static class MetadataServiceExtensions
{
    public static IServiceCollection AddMetadata(
        this IServiceCollection services,
        IConfiguration _)
    {
        services.AddMemoryCache();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<ParameterMetadataService>());

        // Existing services
        services.AddScoped<IParameterMetadataService, ParameterMetadataService>();
        services.AddScoped<INodeMetadataService, NodeMetadataService>();
        services.AddScoped<ITriggerMetadataService, TriggerMetadataService>();
        services.AddScoped<IRouterMetadataService, RouterMetadataService>();
        services.AddScoped<IChannelMetadataService, ChannelMetadataService>();
        services.AddScoped<INodeStateMachine, NodeStateMachine>();
        services.AddScoped<IUsersManagementService, UsersManagementService>();

        // Epic 9A — Operational Read APIs
        services.AddSingleton<IErrorSeverityClassifier, ErrorSeverityClassifier>();
        services.AddScoped<IEventQueryService, EventQueryService>();
        services.AddScoped<IIncomingBatchQueryService, IncomingBatchQueryService>();
        services.AddScoped<IBatchErrorQueryService, BatchErrorQueryService>();
        services.AddScoped<IValidator<EventFilter>, EventFilterValidator>();
        services.AddScoped<IValidator<IncomingBatchFilter>, IncomingBatchFilterValidator>();
        services.AddScoped<IValidator<BatchErrorFilter>, BatchErrorFilterValidator>();

        // Epic 9B — Topology APIs
        services.AddScoped<ITopologyQueryService, TopologyQueryService>();

        return services;
    }
}
```

- [ ] **Step 3: Build full solution**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Create TopologyFixture.cs**

Create `tests/MSOSync.IntegrationTests/Topology/TopologyFixture.cs`:

```csharp
using System.Net.Http.Json;
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
using Xunit;

namespace MSOSync.IntegrationTests.Topology;

public sealed class TopologyFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncTopology_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public string ViewerUsername { get; } = "topology-viewer";
    public string ViewerPassword { get; } = "TopologyP@ss1!";

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

        await SeedTopologyDataAsync(db);
    }

    private static async Task SeedTopologyDataAsync(AppDbContext db)
    {
        if (await db.NodeGroups.AnyAsync()) return;

        // 3 groups: hub (2 nodes), store (1 node), empty (0 nodes)
        db.NodeGroups.AddRange(
            new SyncNodeGroup { GroupId = "group-hub",   GroupName = "Hub"         },
            new SyncNodeGroup { GroupId = "group-store", GroupName = "Store"       },
            new SyncNodeGroup { GroupId = "group-empty", GroupName = "Empty Group" });
        await db.SaveChangesAsync();

        // Nodes: hub has 1 Reachable + 1 Degraded; store has 1 Reachable
        db.Nodes.AddRange(
            new SyncNode { NodeId = "hub-1",   GroupId = "group-hub",   SyncUrl = "http://hub-1",   Status = "REGISTERED", ConnectivityStatus = ConnectivityStatus.Reachable },
            new SyncNode { NodeId = "hub-2",   GroupId = "group-hub",   SyncUrl = "http://hub-2",   Status = "REGISTERED", ConnectivityStatus = ConnectivityStatus.Degraded  },
            new SyncNode { NodeId = "store-1", GroupId = "group-store", SyncUrl = "http://store-1", Status = "REGISTERED", ConnectivityStatus = ConnectivityStatus.Reachable });
        await db.SaveChangesAsync();

        // Router: hub → store
        db.Routers.Add(new SyncRouter
        {
            RouterId        = "router-hub-store",
            SourceNodeGroup = "group-hub",
            TargetNodeGroup = "group-store",
            Enabled         = true
        });
        await db.SaveChangesAsync();

        // Triggers with 2 distinct channels
        db.Triggers.AddRange(
            new SyncTrigger { TriggerId = "trig-1", SourceTable = "dbo.Product", ChannelId = "ch-default" },
            new SyncTrigger { TriggerId = "trig-2", SourceTable = "dbo.Config",  ChannelId = "ch-config"  });
        await db.SaveChangesAsync();

        // TriggerRouters linking both triggers to the router
        db.TriggerRouters.AddRange(
            new SyncTriggerRouter { TriggerId = "trig-1", RouterId = "router-hub-store" },
            new SyncTriggerRouter { TriggerId = "trig-2", RouterId = "router-hub-store" });
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
            "ALTER DATABASE [MSOSyncTopology_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await db.Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("Topology")]
public sealed class TopologyCollection : ICollectionFixture<TopologyFixture> { }
```

- [ ] **Step 5: Create TopologyTests.cs**

Create `tests/MSOSync.IntegrationTests/Topology/TopologyTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Metadata.Topology;
using MSOSync.Persistence;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace MSOSync.IntegrationTests.Topology;

[Collection("Topology")]
public sealed class TopologyTests(TopologyFixture fixture)
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
    public async Task GetGraph_ReturnsEdgesWithChannelIds()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<TopologyGraphDto>("api/v1/topology/graph");

        result!.Edges.Should().HaveCount(1);
        result.Edges.Single().RouterId.Should().Be("router-hub-store");
        result.Edges.Single().ChannelIds.Should().BeEquivalentTo(
            new[] { "ch-default", "ch-config" });
    }

    [Fact]
    public async Task GetGraph_AggregatesConnectivityCorrectly()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<TopologyGraphDto>("api/v1/topology/graph");

        var hub = result!.Nodes.Single(n => n.GroupId == "group-hub");
        hub.TotalNodes.Should().Be(2);
        hub.ReachableNodes.Should().Be(1);
        hub.DegradedNodes.Should().Be(1);
        // Worst-of-members: Degraded (no Unreachable)
        hub.ConnectivityStatus.Should().Be(ConnectivityStatus.Degraded);

        var empty = result.Nodes.Single(n => n.GroupId == "group-empty");
        empty.TotalNodes.Should().Be(0);
        empty.ConnectivityStatus.Should().Be(ConnectivityStatus.Unknown);
    }

    [Fact]
    public async Task GetSummary_ReturnsExpectedCounts()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<TopologySummaryDto>("api/v1/topology/summary");

        result!.TotalGroups.Should().Be(3);
        result.TotalNodes.Should().Be(3);
        result.ReachableNodes.Should().Be(2);
        result.DegradedNodes.Should().Be(1);
        result.UnreachableNodes.Should().Be(0);
    }

    [Fact]
    public async Task GetGroups_ReturnsAllGroups()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<IReadOnlyList<TopologyGroupDto>>("api/v1/topology/groups");

        result!.Should().HaveCount(3);
        result.Select(g => g.GroupId).Should().Contain(
            new[] { "group-hub", "group-store", "group-empty" });
    }

    [Fact]
    public async Task GetGroup_Missing_Returns404()
    {
        var client = await AuthenticatedClientAsync();

        var resp = await client.GetAsync("api/v1/topology/groups/nonexistent-group");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetGroupNodes_ReturnsMembers()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<IReadOnlyList<TopologyGroupNodeDto>>(
            "api/v1/topology/groups/group-hub/nodes");

        result!.Should().HaveCount(2);
        result.Select(n => n.NodeId).Should().BeEquivalentTo(new[] { "hub-1", "hub-2" });
    }

    [Fact]
    public async Task GetGroupNodes_EmptyGroup_ReturnsEmptyArray()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<IReadOnlyList<TopologyGroupNodeDto>>(
            "api/v1/topology/groups/group-empty/nodes");

        result!.Should().BeEmpty();
    }

    [Fact]
    public async Task Unauthorized_Returns401()
    {
        var client = fixture.CreateClient();

        var resp = await client.GetAsync("api/v1/topology/graph");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 6: Run integration tests**

```powershell
$env:DOTNET_ROOT            = "C:\Users\zmehmood\.dotnet"
$env:PATH                   = "C:\Users\zmehmood\.dotnet;$env:PATH"
$env:MSOSYNC_JWT_SECRET     = "test-jwt-secret-value-at-least-32-chars!"
dotnet test tests\MSOSync.IntegrationTests -c Debug --logger "console;verbosity=normal" --filter "FullyQualifiedName~Topology"
```

Expected: 8/8 PASS.

If any test fails with a routing issue, verify `TopologyController.cs` is in the same assembly as `AuthController` (i.e., `MSOSync.Api`) — the fixture's `AddApplicationPart(typeof(AuthController).Assembly)` picks up all controllers in that assembly.

If `TopologyGroupDto` cannot be deserialized (property names differ), verify that `GetFromJsonAsync<T>` uses camelCase JSON (ASP.NET Core defaults). The sealed records use PascalCase property names; `System.Text.Json` by default uses `JsonNamingPolicy.CamelCase` in ASP.NET Core.

- [ ] **Step 7: Run full integration suite to check for regressions**

```powershell
dotnet test tests\MSOSync.IntegrationTests -c Debug --logger "console;verbosity=normal"
```

Expected: all previously green tests still PASS.

- [ ] **Step 8: Run full MetadataTests suite**

```powershell
dotnet test tests\MSOSync.MetadataTests -c Debug --logger "console;verbosity=normal"
```

Expected: all previously green tests still PASS.

- [ ] **Step 9: Commit**

```powershell
git add src/MSOSync.Api/Controllers/TopologyController.cs
git add src/MSOSync.Metadata/MetadataServiceExtensions.cs
git add tests/MSOSync.IntegrationTests/Topology/TopologyFixture.cs
git add tests/MSOSync.IntegrationTests/Topology/TopologyTests.cs
git commit -m "feat(9b): add TopologyController, wire ITopologyQueryService, add 8 integration tests"
```

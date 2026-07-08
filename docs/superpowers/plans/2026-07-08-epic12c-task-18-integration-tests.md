# Task 18: Integration Tests — Overview, Operations, Workers, Correlation, Admin, Navigation, Performance

**Epic:** 12C System Administration Center
**Depends on:** Tasks 13–17 (all backend endpoints must exist), existing integration test infrastructure (look at ConfigurationFixture.cs for the pattern)
**Blocks:** Merge to main

---

## Goal

Write integration tests covering the seven backend surface areas introduced in Epic 12C: system overview, operation registry, worker status, correlation timeline, administration parameters, navigation redirects, and performance under load.

---

## Step 1 — Read existing fixture pattern

- [ ] Find the existing integration test fixture:

```powershell
Get-ChildItem -Recurse -Path tests/MSOSync.IntegrationTests -Include "*Fixture*" | Select-Object FullName
```

- [ ] Open the first result (likely `ConfigurationFixture.cs` or `NodeFixture.cs`). Note:
  - The `WebApplicationFactory<Program>` usage
  - How the test database connection string is configured
  - How admin users are seeded (roles, permissions)
  - How authenticated `HttpClient` is obtained (JWT token generation or test identity setup)
  - The `IAsyncLifetime` implementation pattern
  - How `AppDbContext` is obtained from `fx.Services`

Use the exact same patterns in `SystemFixture.cs`.

---

## Step 2 — Read existing test class pattern

- [ ] Open one existing integration test class (e.g., `ConfigurationTests.cs` or `AuditTests.cs`). Note:
  - The `[Collection("...")]` attribute used to share the fixture
  - The `JsonSerializerOptions` (`JsonOpts`) used for deserialization
  - How response DTOs are deserialized
  - Whether `FluentAssertions` is used (look for `.Should()`)

---

## Step 3 — Locate the test project file

- [ ] Run:

```powershell
Get-ChildItem -Recurse -Path tests/MSOSync.IntegrationTests -Include "*.csproj" | Select-Object FullName
```

Open the `.csproj` file. Verify these packages are referenced:
- `Microsoft.AspNetCore.Mvc.Testing`
- `xunit`
- `FluentAssertions`
- `Microsoft.EntityFrameworkCore` (for direct DB access in tests)

If any are missing, add them to the `.csproj` before writing tests.

---

## Step 4 — Create SystemFixture.cs

- [ ] Create `tests/MSOSync.IntegrationTests/System/SystemFixture.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Infrastructure.Data;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace MSOSync.IntegrationTests.System;

// Use the same collection name pattern as other fixture collections in this project.
// Read existing test classes in Step 2 to find the correct collection name format.
[CollectionDefinition("System")]
public class SystemCollection : ICollectionFixture<SystemFixture> { }

public class SystemFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Read the existing fixture to find the exact DB connection string override pattern.
    // The pattern below is a template — adjust to match what the project uses.

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            // Add test database
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(
                    "Server=(localdb)\\mssqllocaldb;Database=MSOSyncSystem_Test;Trusted_Connection=True;",
                    sql => sql.MigrationsAssembly("MSOSync.Infrastructure")));
        });
    }

    public async Task InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        await SeedAsync(db);
    }

    public new async Task DisposeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        // Seed roles
        // Adjust entity names to match the actual EF entities in the project.
        // Read AppDbContext to find the correct DbSet names.

        // Seed: ADMIN, OPERATOR, VIEWER roles
        // Seed: all permissions attached to ADMIN
        // Seed: 1 node group (required FK for nodes)
        // Seed: admin user (username: "test-admin", role: ADMIN)
        // Seed: viewer user (username: "test-viewer", role: VIEWER)

        // NOTE: The exact seeding code depends on the entity model.
        // Copy the seeding pattern from the existing fixture (found in Step 1).
        // The pattern is the same — only the database name differs.

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Returns an authenticated HttpClient for the ADMIN user.
    /// Copy the exact token generation pattern from the existing fixture.
    /// </summary>
    public async Task<HttpClient> AdminClientAsync()
    {
        // Pattern from existing fixture: generate a JWT for the admin user and attach it.
        // Example (adjust to match actual auth implementation):
        var client = CreateClient();
        var token = await GetTokenAsync("test-admin", "Admin123!");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Returns an authenticated HttpClient for the VIEWER user.
    /// </summary>
    public async Task<HttpClient> ViewerClientAsync()
    {
        var client = CreateClient();
        var token = await GetTokenAsync("test-viewer", "Viewer123!");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<string> GetTokenAsync(string username, string password)
    {
        // Copy the login endpoint call pattern from the existing fixture.
        // Typically: POST /api/v1/auth/login with { username, password }
        // Returns: { token: "..." }
        var loginClient = CreateClient();
        var resp = await loginClient.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { username, password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    /// <summary>
    /// Seeds N sync_operation rows with varied statuses for performance tests.
    /// </summary>
    public async Task SeedOperationsAsync(int count)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var statuses = new[] { "Pending", "Running", "Completed", "Failed", "Cancelled" };
        var types = new[] { "Export", "Rollout", "Decommission", "Recovery" };
        var rng = new Random(42);

        var operations = Enumerable.Range(0, count).Select(i =>
        {
            // Adjust entity and property names to match the actual Operation entity.
            // Read the Operation entity file to find correct property names.
            return new SyncOperation
            {
                OperationId = Guid.NewGuid(),
                OperationType = types[i % types.Length],
                Status = statuses[i % statuses.Length],
                StartedAt = DateTime.UtcNow.AddMinutes(-rng.Next(1, 10000)),
                CorrelationId = Guid.NewGuid().ToString(),
            };
        }).ToList();

        await db.Operations.AddRangeAsync(operations);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds N sync_node rows with varied states for performance tests.
    /// </summary>
    public async Task SeedNodesAsync(int count)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Copy the node seeding pattern from the existing fixture.
        // Adjust entity and property names as needed.
        await db.SaveChangesAsync();
    }

    private record LoginResponse(string Token);
}
```

**IMPORTANT:** This fixture is a template. Before using it, open the existing fixture from Step 1 and copy:
1. The exact `ConfigureWebHost` override pattern for replacing DbContext
2. The exact seeding pattern for roles, users, and permissions
3. The exact login/token endpoint and response shape
4. The exact entity class names (`SyncOperation`, `AppDbContext.Operations`, etc.)

---

## Step 5 — Create OverviewTests.cs

- [ ] Create `tests/MSOSync.IntegrationTests/System/OverviewTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.System;

[Collection("System")]
public class OverviewTests
{
    private readonly SystemFixture fx;

    // JSON options — copy from existing test class in Step 2.
    // Typically: JsonSerializerOptions with PropertyNameCaseInsensitive = true
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public OverviewTests(SystemFixture fx)
    {
        this.fx = fx;
    }

    [Fact]
    public async Task GetOverview_ReturnsAllWidgets()
    {
        var admin = await fx.AdminClientAsync();
        var resp = await admin.GetAsync("/api/v1/system/overview");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Deserialize to a dynamic/anonymous type to avoid coupling to DTO class directly.
        // Adjust property names to match the actual backend response JSON keys.
        var body = await resp.Content.ReadFromJsonAsync<OverviewResponse>(JsonOpts);
        body.Should().NotBeNull();
        body!.Health.Should().NotBeNull("overview must include a health section");
        body.Operations.Should().NotBeNull("overview must include an operations section");
        body.Nodes.Should().NotBeNull("overview must include a nodes section");
        body.LastRefreshedAt.Should().NotBeNullOrEmpty("overview must include a timestamp");
    }

    [Fact]
    public async Task GetOverview_Viewer_Returns200()
    {
        var viewer = await fx.ViewerClientAsync();
        var resp = await viewer.GetAsync("/api/v1/system/overview");
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "VIEWER role should have read-only access to the overview");
    }

    [Fact]
    public async Task GetOverview_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/system/overview");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSystemInfo_ReturnsVersionAndEdition()
    {
        var viewer = await fx.ViewerClientAsync();
        var resp = await viewer.GetAsync("/api/v1/system/info");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var info = await resp.Content.ReadFromJsonAsync<SystemInfoResponse>(JsonOpts);
        info!.Edition.Should().Be("Community");
        info.AppVersion.Should().NotBeNullOrEmpty();
        info.Environment.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetSystemInfo_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/system/info");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSystemHealth_ReturnsContributors()
    {
        var admin = await fx.AdminClientAsync();
        var resp = await admin.GetAsync("/api/v1/system/health");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var contributors = await resp.Content.ReadFromJsonAsync<HealthContributorResponse[]>(JsonOpts);
        contributors.Should().NotBeNullOrEmpty("at least one health contributor must be registered");
        contributors!.Should().AllSatisfy(c =>
        {
            c.Contributor.Should().NotBeNullOrEmpty();
            c.Level.Should().BeOneOf("Healthy", "Degraded", "Critical", "Unknown");
        });
    }

    // --- Inner response record types ---
    // Adjust property names to match the actual JSON returned by the backend.

    private record OverviewResponse(
        HealthSummaryResp Health,
        OperationsSummaryResp Operations,
        NodesSummaryResp Nodes,
        string LastRefreshedAt);

    private record HealthSummaryResp(string ClusterHealth, string WorkerHealth, string NodeHealth);
    private record OperationsSummaryResp(int ActiveJobCount);
    private record NodesSummaryResp(int TotalNodes, int ActiveNodes);
    private record SystemInfoResponse(string AppVersion, string Edition, string Environment);
    private record HealthContributorResponse(string Contributor, string Level, string? Detail);
}
```

---

## Step 6 — Create OperationRegistryTests.cs

- [ ] Create `tests/MSOSync.IntegrationTests/System/OperationRegistryTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Infrastructure.Data;
using Xunit;

namespace MSOSync.IntegrationTests.System;

[Collection("System")]
public class OperationRegistryTests
{
    private readonly SystemFixture fx;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public OperationRegistryTests(SystemFixture fx)
    {
        this.fx = fx;
    }

    [Fact]
    public async Task ListOperations_EmptyDb_ReturnsEmptyPage()
    {
        var admin = await fx.AdminClientAsync();
        var resp = await admin.GetAsync("/api/v1/operations");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await resp.Content.ReadFromJsonAsync<OperationPageResponse>(JsonOpts);
        page.Should().NotBeNull();
        page!.Items.Should().NotBeNull();
        page.TotalCount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ListOperations_WithTypeFilter_ReturnsCorrectSubset()
    {
        // Seed 2 Export + 2 Rollout operations directly into the DB
        await using var scope = fx.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Adjust entity class and property names to match the actual model.
        var exportId1 = Guid.NewGuid();
        var exportId2 = Guid.NewGuid();
        // Insert directly: db.Operations.AddRange(...)
        // Use the same SyncOperation entity found in Step 1 of SystemFixture setup.
        // Set OperationType = "Export" for 2, "Rollout" for 2.
        await db.SaveChangesAsync();

        var admin = await fx.AdminClientAsync();
        var resp = await admin.GetAsync("/api/v1/operations?types=Export");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<OperationPageResponse>(JsonOpts);
        page!.Items.Should().AllSatisfy(op =>
            op.OperationType.Should().Be("Export"),
            "type filter must exclude non-Export operations");
    }

    [Fact]
    public async Task CancelOperation_SetsStatusCancelled()
    {
        // Insert a Running operation
        await using var scope = fx.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var opId = Guid.NewGuid();
        // db.Operations.Add(new SyncOperation { OperationId = opId, Status = "Running", CanCancel = true, ... });
        // Adjust to match actual entity.
        await db.SaveChangesAsync();

        var admin = await fx.AdminClientAsync();
        var resp = await admin.PostAsync($"/api/v1/operations/{opId}/cancel", null);

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // Verify DB state
        await using var scope2 = fx.Services.CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var op = await db2.Operations.FindAsync(opId);
        op!.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task GetOperation_NotFound_Returns404()
    {
        var admin = await fx.AdminClientAsync();
        var resp = await admin.GetAsync($"/api/v1/operations/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListOperations_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/operations");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // --- Response types ---
    private record OperationPageResponse(OperationItemResponse[] Items, int TotalCount);
    private record OperationItemResponse(string OperationId, string OperationType, string Status);
}
```

---

## Step 7 — Create WorkerStatusTests.cs

- [ ] Create `tests/MSOSync.IntegrationTests/System/WorkerStatusTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MSOSync.IntegrationTests.System;

/// <summary>
/// Worker status tests operate in-process against the DI container.
/// No HTTP endpoint is needed — workers register themselves on startup
/// via IWorkerStatusRegistry.
/// </summary>
[Collection("System")]
public class WorkerStatusTests
{
    private readonly SystemFixture fx;

    public WorkerStatusTests(SystemFixture fx)
    {
        this.fx = fx;
    }

    [Fact]
    public async Task Workers_RegisteredOnStartup_AppearInGetAll()
    {
        await using var scope = fx.Services.CreateAsyncScope();

        // IWorkerStatusRegistry is the interface that exposes GetAll().
        // Find the exact interface name by searching:
        // Get-ChildItem -Recurse -Path src -Include "*.cs" | Select-String "interface.*WorkerStatus\|IWorkerStatus"
        // Adjust the type parameter below to match the actual interface name.
        var registry = scope.ServiceProvider.GetRequiredService<IWorkerStatusRegistry>();

        var workers = registry.GetAll();
        workers.Should().NotBeEmpty(
            "background workers must register themselves with the registry on application startup");
    }

    [Fact]
    public async Task Workers_GetAll_IncludeWorkerNames()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IWorkerStatusRegistry>();

        var workers = registry.GetAll();
        workers.Should().AllSatisfy(w =>
            w.WorkerName.Should().NotBeNullOrEmpty("each worker must have a non-empty name"));
    }

    [Fact]
    public async Task Workers_GetAll_HaveValidStates()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IWorkerStatusRegistry>();

        var validStates = new[] { "Running", "Idle", "Warning", "Failed", "Delayed", "Disabled" };
        var workers = registry.GetAll();
        workers.Should().AllSatisfy(w =>
            validStates.Should().Contain(w.WorkerState,
                $"worker {w.WorkerName} has an unrecognized state '{w.WorkerState}'"));
    }

    [Fact]
    public async Task GetWorkers_HttpEndpoint_Returns200()
    {
        var admin = await fx.AdminClientAsync();
        var resp = await admin.GetAsync("/api/v1/system/workers");
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }
}
```

---

## Step 8 — Create CorrelationTimelineTests.cs

- [ ] Create `tests/MSOSync.IntegrationTests/System/CorrelationTimelineTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Infrastructure.Data;
using Xunit;

namespace MSOSync.IntegrationTests.System;

[Collection("System")]
public class CorrelationTimelineTests
{
    private readonly SystemFixture fx;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public CorrelationTimelineTests(SystemFixture fx)
    {
        this.fx = fx;
    }

    [Fact]
    public async Task GetCorrelation_WithAuditEvents_ReturnsTimeline()
    {
        // Seed 3 audit rows with the same correlation_id
        var correlationId = Guid.NewGuid().ToString();

        await using var scope = fx.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Adjust entity class and property names to match the actual audit entity.
        // The entity is likely SyncAudit or AuditLog with properties:
        // CorrelationId, ActionName, Summary, OccurredAt, Category, Severity, ActorUsername
        // Find the exact class by searching:
        // Get-ChildItem -Recurse -Path src -Include "*.cs" | Select-String "class SyncAudit\|class AuditLog\|class AuditEntry"
        for (int i = 0; i < 3; i++)
        {
            db.Audits.Add(new SyncAudit
            {
                CorrelationId = correlationId,
                ActionName = $"TEST_ACTION_{i}",
                Summary = $"Test event {i} for correlation test",
                OccurredAt = DateTime.UtcNow.AddSeconds(-i * 10),
                Category = "System",
                Severity = "Information",
                ActorUsername = "test-admin",
            });
        }
        await db.SaveChangesAsync();

        var admin = await fx.AdminClientAsync();
        var resp = await admin.GetAsync($"/api/v1/audit/correlation/{Uri.EscapeDataString(correlationId)}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var timeline = await resp.Content.ReadFromJsonAsync<CorrelationTimelineResponse>(JsonOpts);
        timeline.Should().NotBeNull();
        timeline!.CorrelationId.Should().Be(correlationId);
        timeline.TotalEventCount.Should().Be(3);
        timeline.Phases.Should().NotBeEmpty("events must be grouped into phases");
    }

    [Fact]
    public async Task GetCorrelation_NotFound_Returns404()
    {
        var admin = await fx.AdminClientAsync();
        var resp = await admin.GetAsync("/api/v1/audit/correlation/nonexistent-correlation-id-that-does-not-exist");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SearchCorrelations_ByCorrelationIdFragment_ReturnsMatch()
    {
        var correlationId = Guid.NewGuid().ToString();

        await using var scope = fx.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Audits.Add(new SyncAudit
        {
            CorrelationId = correlationId,
            ActionName = "SEARCH_TEST",
            Summary = "Search test event",
            OccurredAt = DateTime.UtcNow,
            Category = "System",
            Severity = "Information",
        });
        await db.SaveChangesAsync();

        var admin = await fx.AdminClientAsync();
        var resp = await admin.GetAsync(
            $"/api/v1/audit/correlation/search?correlationId={Uri.EscapeDataString(correlationId)}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var results = await resp.Content.ReadFromJsonAsync<CorrelationSearchResponse[]>(JsonOpts);
        results.Should().Contain(r => r.CorrelationId == correlationId);
    }

    [Fact]
    public async Task GetCorrelation_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/audit/correlation/some-id");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // --- Response types ---
    private record CorrelationTimelineResponse(
        string CorrelationId,
        int TotalEventCount,
        bool IsFailedWorkflow,
        CorrelationPhaseResponse[] Phases);

    private record CorrelationPhaseResponse(string PhaseName, object[] Events);
    private record CorrelationSearchResponse(string CorrelationId, string? OperationType, int TotalEventCount);
}
```

---

## Step 9 — Create AdministrationTests.cs

- [ ] Create `tests/MSOSync.IntegrationTests/System/AdministrationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Infrastructure.Data;
using Xunit;

namespace MSOSync.IntegrationTests.System;

[Collection("System")]
public class AdministrationTests
{
    private readonly SystemFixture fx;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public AdministrationTests(SystemFixture fx)
    {
        this.fx = fx;
    }

    [Fact]
    public async Task GetParameters_WithFeatureFlagCategory_ReturnsOnlyFlags()
    {
        var admin = await fx.AdminClientAsync();
        var resp = await admin.GetAsync("/api/v1/parameters?category=FeatureFlag");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await resp.Content.ReadFromJsonAsync<ParameterResponse[]>(JsonOpts);
        items.Should().NotBeNull();
        items!.Should().AllSatisfy(p =>
            p.Category.Should().Be("FeatureFlag",
                "category filter must only return parameters in the requested category"));
    }

    [Fact]
    public async Task GetParameters_WithRetentionCategory_ReturnsOnlyRetentionParams()
    {
        var admin = await fx.AdminClientAsync();
        var resp = await admin.GetAsync("/api/v1/parameters?category=Retention");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await resp.Content.ReadFromJsonAsync<ParameterResponse[]>(JsonOpts);
        items.Should().NotBeNull();
        // Retention parameters may be empty if none are seeded.
        // The test verifies the endpoint works and filters correctly.
        if (items!.Length > 0)
        {
            items.Should().AllSatisfy(p =>
                p.Category.Should().Be("Retention"));
        }
    }

    [Fact]
    public async Task UpdateParameter_Viewer_Returns403()
    {
        // Verify that VIEWER cannot update parameters (requires ManageConfigurations permission)
        var viewer = await fx.ViewerClientAsync();
        var resp = await viewer.PutAsJsonAsync(
            "/api/v1/parameters/SomeParam",
            new { value = "test" });

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized,
            "VIEWER role must not be able to modify parameters");
    }

    [Fact]
    public async Task UpdateParameter_Admin_Returns200AndGeneratesAuditEvent()
    {
        // First, find a parameter that exists (GET all parameters)
        var admin = await fx.AdminClientAsync();
        var listResp = await admin.GetAsync("/api/v1/parameters");
        listResp.EnsureSuccessStatusCode();

        var items = await listResp.Content.ReadFromJsonAsync<ParameterResponse[]>(JsonOpts);
        if (items == null || items.Length == 0)
        {
            // No parameters seeded — skip the update test but don't fail
            return;
        }

        var param = items[0];
        var newValue = param.Value == "true" ? "false" : "true";

        var resp = await admin.PutAsJsonAsync(
            $"/api/v1/parameters/{Uri.EscapeDataString(param.Name)}",
            new { value = newValue });

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // Verify audit event was generated
        await using var scope = fx.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var auditRow = await db.Audits
            .AsNoTracking()
            .Where(a => a.ActionName == "PARAMETER_UPDATED" &&
                        a.ObjectName != null &&
                        a.ObjectName.Contains(param.Name))
            .FirstOrDefaultAsync();

        auditRow.Should().NotBeNull(
            $"updating parameter '{param.Name}' must generate a PARAMETER_UPDATED audit event");
    }

    [Fact]
    public async Task GetSystemInfo_ReturnsVersionAndEdition()
    {
        var viewer = await fx.ViewerClientAsync();
        var resp = await viewer.GetAsync("/api/v1/system/info");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var info = await resp.Content.ReadFromJsonAsync<SystemInfoResponse>(JsonOpts);
        info!.Edition.Should().Be("Community");
        info.AppVersion.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetParameters_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/parameters");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // --- Response types ---
    private record ParameterResponse(string Name, string? Value, string? Category, string? DisplayName);
    private record SystemInfoResponse(string AppVersion, string Edition, string Environment);
}
```

---

## Step 10 — Create NavigationRedirectTests.cs

- [ ] Create `tests/MSOSync.IntegrationTests/System/NavigationRedirectTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.System;

/// <summary>
/// Navigation redirect tests verify backend HTTP endpoints respond correctly.
/// Frontend route redirects (Navigate component) are covered by the build
/// type check and manual smoke tests in Task 12.
/// </summary>
[Collection("System")]
public class NavigationRedirectTests
{
    private readonly SystemFixture fx;

    public NavigationRedirectTests(SystemFixture fx)
    {
        this.fx = fx;
    }

    [Fact]
    public async Task SystemInfo_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/system/info");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SystemOverview_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/system/overview");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SystemWorkers_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/system/workers");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SystemHealth_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/system/health");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CorrelationTimeline_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/audit/correlation/some-id");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Operations_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/operations");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Parameters_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/parameters");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

---

## Step 11 — Create OverviewPerformanceTests.cs

- [ ] Create `tests/MSOSync.IntegrationTests/System/OverviewPerformanceTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.System;

[Collection("System")]
public class OverviewPerformanceTests
{
    private readonly SystemFixture fx;

    public OverviewPerformanceTests(SystemFixture fx)
    {
        this.fx = fx;
    }

    [Fact]
    public async Task GetOverview_With1000Operations_RespondsWithin500ms()
    {
        // Seed 1000 operations and 100 nodes
        await fx.SeedOperationsAsync(1000);
        await fx.SeedNodesAsync(100);

        var admin = await fx.AdminClientAsync();

        // Warm up: one request before timing (avoids cold-start skewing the measurement)
        await admin.GetAsync("/api/v1/system/overview");

        // Timed request
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resp = await admin.GetAsync("/api/v1/system/overview");
        sw.Stop();

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            "overview must respond in under 500ms with 1000 operations and 100 nodes seeded");
    }

    [Fact]
    public async Task GetWorkers_WithAllWorkersRegistered_RespondsWithin300ms()
    {
        var admin = await fx.AdminClientAsync();

        // Warm up
        await admin.GetAsync("/api/v1/system/workers");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resp = await admin.GetAsync("/api/v1/system/workers");
        sw.Stop();

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(300,
            "workers endpoint must respond in under 300ms regardless of tick history count");
    }

    [Fact]
    public async Task GetOverview_CalledTenTimesInSequence_NoMemoryLeakIndicators()
    {
        // This test is not a strict memory test — it verifies the endpoint handles
        // repeated calls without throwing or degrading significantly.
        var admin = await fx.AdminClientAsync();

        var times = new List<long>();
        for (int i = 0; i < 10; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var resp = await admin.GetAsync("/api/v1/system/overview");
            sw.Stop();
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            times.Add(sw.ElapsedMilliseconds);
        }

        // The last call should not be dramatically slower than the first.
        // Allow 5x variance as a loose sanity check.
        var first = times[0];
        var last = times[^1];
        last.Should().BeLessThan(Math.Max(first * 5, 2000),
            "repeated calls to overview should not degrade significantly");
    }
}
```

---

## Step 12 — Build the test project

- [ ] Run:

```powershell
cd tests/MSOSync.IntegrationTests && dotnet build 2>&1
```

Common compile errors and fixes:

1. **Entity class name wrong** (`SyncAudit` doesn't exist): Run the search from Step 1 comment inside `CorrelationTimelineTests.cs` to find the real audit entity class name. Replace `SyncAudit` with the correct name.

2. **DbSet name wrong** (`db.Audits` doesn't exist): Open `AppDbContext.cs` and find the correct `DbSet<>` property name for audits (may be `db.AuditEntries`, `db.AuditLogs`, etc.).

3. **IWorkerStatusRegistry not found**: Search for the interface:

```powershell
Get-ChildItem -Recurse -Path src -Include "*.cs" | Select-String "interface.*WorkerStatus\|IWorkerStatus" | Select-Object -First 3
```

Replace `IWorkerStatusRegistry` with the actual interface name.

4. **`GetAll()` method not found**: The actual method may be `GetAllWorkers()`, `All()`, or similar. Check the interface definition.

5. **`SyncOperation` entity not found**: Search for the operation entity class name:

```powershell
Get-ChildItem -Recurse -Path src -Include "*.cs" | Select-String "class.*Operation\b" | Select-Object -First 5
```

---

## Step 13 — Run the tests

- [ ] Run all System tests:

```powershell
cd tests/MSOSync.IntegrationTests && dotnet test --filter "FullyQualifiedName~MSOSync.IntegrationTests.System" --logger "console;verbosity=normal" 2>&1
```

Expected outcome: all tests pass. Fix any failures before proceeding.

Common runtime failures:

- **401 when 200 expected**: Token generation failed. Check that the seeded admin user credentials match what `GetTokenAsync` sends.
- **404 on `/api/v1/system/overview`**: The backend controller route is different. Search for `[Route("api/v1/system")]` in the backend to find the exact path.
- **`IWorkerStatusRegistry` not registered**: The interface may need to be registered in `Program.cs`. This is a backend bug to fix, not a test bug.
- **Performance test fails (>500ms)**: Add the relevant DB indexes (e.g., on `sync_operation.status`, `sync_operation.started_at`) as a migration. This is a backend optimization.

---

## Step 14 — Commit

- [ ] Stage files:

```powershell
git add tests/MSOSync.IntegrationTests/System/
```

- [ ] Commit:

```powershell
git commit -m "test(12C-18): integration tests for Overview, Operations, Workers, Correlation, Admin, Performance"
```
